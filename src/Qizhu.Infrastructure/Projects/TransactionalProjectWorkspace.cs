using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;
using Qizhu.Domain.Projects;
using Qizhu.Infrastructure.Auditing;
using Qizhu.Infrastructure.Common;
using Qizhu.Infrastructure.Database;
using Qizhu.Infrastructure.Identity;
using Qizhu.Infrastructure.Tenancy;
using SqlSugar;

namespace Qizhu.Infrastructure.Projects;

/// <summary>数据库事务集中保证项目写入、审计和成功请求幂等结果一起提交。</summary>
public sealed class TransactionalProjectWorkspace(IDatabaseConnection connection, ICurrentTenant tenant, ICurrentActor actor,
    ITenantDirectory tenants, IAuditWriter audit, TimeProvider clock, IIdGenerator ids) : IProjectWorkspace
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<ProjectDetails>> ListAsync(ProjectQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.PageNumber < 1 || query.PageSize is < 1 or > 100)
        {
            throw new ApiException(400, "pagination.invalid", "页码必须大于零，每页数量必须在 1 到 100 之间。");
        }
        if (((long)query.PageNumber - 1) * query.PageSize > 10000)
        {
            throw new ApiException(400, "pagination.offset_exceeded", "请缩小查询范围，或使用后台导出。");
        }
        if (query.Keyword?.Length > 100) { throw new ApiException(400, "request.invalid", "搜索内容不能超过 100 个字符。"); }
        var keyword = query.Keyword?.Trim();
        if (query.SortBy is not ("createdAt" or "updatedAt" or "name") || query.SortDirection is not ("asc" or "desc"))
        {
            throw new ApiException(400, "request.invalid", "排序字段或方向不正确。");
        }
        if (query.Deleted && !(await tenants.GetPermissionsAsync(actor.UserId, tenant.Id)).Contains("projects.projects.restore"))
        {
            throw new ApiException(403, "auth.forbidden", "查看回收站需要恢复权限。");
        }
        // Contains 翻译为 SqlFunc.CharIndex > 0，通配符按普通字符查询，不拼接 SQL。
        var from = query.CreatedFrom?.ToUniversalTime() ?? DateTimeOffset.MinValue;
        var to = query.CreatedTo?.ToUniversalTime() ?? DateTimeOffset.MaxValue;
        if (from >= to) { throw new ApiException(400, "request.invalid", "结束时间必须晚于开始时间。"); }
        var total = new RefAsync<int>();
        var list = connection.Client.Queryable<ProjectRecord>().Where(item => item.TenantId == tenant.Id && item.IsDeleted == query.Deleted)
            .WhereIF(!string.IsNullOrEmpty(keyword), item => SqlFunc.CharIndexNew(item.Name, keyword!) > 0)
            .WhereIF(query.CreatedFrom.HasValue, item => item.CreatedAt >= from)
            .WhereIF(query.CreatedTo.HasValue, item => item.CreatedAt < to);
        var direction = query.SortDirection == "asc" ? OrderByType.Asc : OrderByType.Desc;
        list = query.SortBy switch
        {
            "name" => list.OrderBy(item => item.Name, direction),
            "updatedAt" => list.OrderBy(item => item.UpdatedAt, direction),
            _ => list.OrderBy(item => item.CreatedAt, direction)
        };
        var records = await list.OrderBy(item => item.Id).ToPageListAsync(query.PageNumber, query.PageSize, total);
        return new PagedResult<ProjectDetails>(records.Select(ToDetails).ToArray(), total.Value, query.PageNumber, query.PageSize);
    }

    public async Task<ProjectDetails?> FindAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await connection.Client.Queryable<ProjectRecord>().Where(item => item.TenantId == tenant.Id && item.Id == id && !item.IsDeleted).SingleAsync();
        return record is null ? null : ToDetails(record);
    }

    public Task<MutationResult> CreateAsync(string name, MutationContext context, CancellationToken cancellationToken) =>
        WriteAsync("projects.projects.create", JsonSerializer.Serialize(new { name }, Json), context, async () =>
        {
            var project = Project.Create(ids.NextId(), tenant.Id, name, clock.GetUtcNow());
            await connection.Client.Insertable(new ProjectRecord
            {
                Id = project.Id,
                TenantId = tenant.Id,
                Name = project.Name,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.CreatedAt,
                CreatedBy = actor.UserId,
                UpdatedBy = actor.UserId
            }).ExecuteCommandAsync();
            var created = await FindAsync(project.Id, cancellationToken) ?? throw new InvalidOperationException("新建项目未能读取。");
            await audit.WriteAsync(tenant.Id, "projects.projects.create", created.Id, "新增项目");
            return new MutationResult(201, created, $"/api/v1/projects/{created.Id}", created.Version);
        }, cancellationToken);

    public Task<MutationResult> UpdateAsync(long id, string name, MutationContext context, CancellationToken cancellationToken) =>
        WriteAsync("projects.projects.update", JsonSerializer.Serialize(new { id, name }, Json), context, async () =>
        {
            var cleanName = Project.NormalizeName(name);
            var expected = VersionToken.Decode(context.IfMatch);
            var now = clock.GetUtcNow();
            var affected = await connection.Client.Updateable<ProjectRecord>().SetColumns(item => new ProjectRecord
            {
                Name = cleanName,
                UpdatedAt = now,
                UpdatedBy = actor.UserId
            }).Where(item => item.Id == id && item.TenantId == tenant.Id && !item.IsDeleted && item.RowVersion == expected).ExecuteCommandAsync();
            await RequireAffectedAsync(affected, id);
            var changed = await FindAsync(id, cancellationToken) ?? throw new InvalidOperationException("修改后项目未能读取。");
            await audit.WriteAsync(tenant.Id, "projects.projects.update", id, "修改项目");
            return new MutationResult(200, changed, null, changed.Version);
        }, cancellationToken);

    public Task<MutationResult> DeleteAsync(long id, MutationContext context, CancellationToken cancellationToken) =>
        ChangeDeletionAsync(id, true, context, cancellationToken);
    public Task<MutationResult> RestoreAsync(long id, MutationContext context, CancellationToken cancellationToken) =>
        ChangeDeletionAsync(id, false, context, cancellationToken);

    private Task<MutationResult> ChangeDeletionAsync(long id, bool deleted, MutationContext context, CancellationToken cancellationToken)
    {
        var operation = deleted ? "projects.projects.delete" : "projects.projects.restore";
        return WriteAsync(operation, JsonSerializer.Serialize(new { id }, Json), context, async () =>
        {
            var expected = VersionToken.Decode(context.IfMatch);
            var now = clock.GetUtcNow();
            DateTimeOffset? deletedAt = deleted ? now : null;
            long? deletedBy = deleted ? actor.UserId : null;
            var affected = await connection.Client.Updateable<ProjectRecord>().SetColumns(item => new ProjectRecord
            {
                IsDeleted = deleted,
                DeletedAt = deletedAt,
                DeletedBy = deletedBy,
                UpdatedAt = now,
                UpdatedBy = actor.UserId
            }).Where(item => item.Id == id && item.TenantId == tenant.Id && item.IsDeleted != deleted && item.RowVersion == expected).ExecuteCommandAsync();
            await RequireAffectedAsync(affected, id);
            await audit.WriteAsync(tenant.Id, operation, id, deleted ? "软删除项目" : "恢复项目");
            var restored = deleted ? null : await FindAsync(id, cancellationToken);
            return new MutationResult(deleted ? 204 : 200, restored, null, restored?.Version);
        }, cancellationToken);
    }

    private async Task RequireAffectedAsync(int count, long id)
    {
        if (count == 1) { return; }
        var exists = await connection.Client.Queryable<ProjectRecord>().Where(item => item.Id == id && item.TenantId == tenant.Id).AnyAsync();
        throw exists ? new ApiException(412, "concurrency.stale", "记录已被其他操作修改，请刷新后重试。")
            : new ApiException(404, "resource.not_found", "记录不存在。");
    }

    private async Task<MutationResult> WriteAsync(string operation, string canonicalInput, MutationContext context,
        Func<Task<MutationResult>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Regex.IsMatch(context.Key, "^[a-zA-Z0-9._:-]{16,128}$", RegexOptions.CultureInvariant))
        {
            throw new ApiException(400, string.IsNullOrEmpty(context.Key) ? "idempotency.key_required" : "idempotency.key_invalid", "请提供有效的幂等键。");
        }
        var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(context.Key));
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(operation + "\n" + canonicalInput + "\n" + context.IfMatch));
        await connection.Client.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            var now = clock.GetUtcNow();
            // 唯一索引 + Serializable 范围锁 + 更新锁，覆盖首次插入的并发请求。
            var existing = await connection.Client.Queryable<IdempotencyRecord>().With(SqlWith.UpdLock)
                .Where(item => item.TenantId == tenant.Id && item.ActorId == actor.UserId && item.Operation == operation && item.KeyHash == keyHash).SingleAsync();
            if (existing is not null && existing.ExpiresAt <= now)
            {
                await connection.Client.Deleteable<IdempotencyRecord>().Where(item => item.Id == existing.Id).ExecuteCommandAsync();
                existing = null;
            }
            var idleLimit = now.AddMinutes(-30);
            // 事务内重新鉴权，Serializable 保留账号、会话、租户和授权读锁直到提交。
            var validSession = await connection.Client.Queryable<SessionRecord>()
                .InnerJoin<FrameworkUser>((session, user) => session.UserId == user.Id)
                .Where((session, user) => session.Id == actor.SessionId && session.UserId == actor.UserId && !session.IsRevoked && user.IsEnabled
                    && session.SecurityStamp == user.SecurityStamp && session.ExpiresAt > now && session.LastSeenAt > idleLimit).AnyAsync();
            if (!validSession) { throw new ApiException(401, "auth.session_expired", "会话已失效，请重新登录。"); }
            await tenants.RequireMembershipAsync(actor.UserId, tenant.Id, writing: true);
            if (!(await tenants.GetPermissionsAsync(actor.UserId, tenant.Id)).Contains(operation))
            {
                throw new ApiException(403, "auth.forbidden", "你没有执行该操作的权限。");
            }

            if (existing is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
                {
                    throw new ApiException(409, "idempotency.key_conflict", "该幂等键已经用于不同请求。");
                }
                var replay = JsonSerializer.Deserialize<MutationResult>(existing.ResponseJson, Json) ?? throw new InvalidOperationException("幂等结果无效。");
                await connection.Client.Ado.CommitTranAsync();
                return replay;
            }

            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            await connection.Client.Insertable(new IdempotencyRecord
            {
                Id = ids.NextId(),
                TenantId = tenant.Id,
                ActorId = actor.UserId,
                Operation = operation,
                KeyHash = keyHash,
                RequestHash = requestHash,
                StatusCode = result.StatusCode,
                ResponseJson = JsonSerializer.Serialize(result, Json),
                Location = result.Location,
                Etag = result.Version,
                ExpiresAt = clock.GetUtcNow().AddHours(24)
            }).ExecuteCommandAsync();
            await connection.Client.Ado.CommitTranAsync();
            return result;
        }
        catch (Exception exception)
        {
            connection.Client.Ado.CancellationToken = null;
            await connection.Client.Ado.RollbackTranAsync();
            if (exception is SqlException { Number: 1222 or 1205 or -2 } || exception.InnerException is SqlException { Number: 1222 or 1205 or -2 })
            {
                throw new ApiException(409, "idempotency.in_progress", "同一操作正在处理，请稍后使用相同幂等键重试。");
            }
            throw;
        }
        finally
        {
            connection.Client.Ado.CancellationToken = null;
        }
    }

    private static ProjectDetails ToDetails(ProjectRecord record) => new(record.Id, record.Name, record.CreatedAt, record.UpdatedAt,
        VersionToken.Encode(record.RowVersion), record.IsDeleted);
}
