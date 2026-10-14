using Qizhu.Application.Common;
using Qizhu.Infrastructure.Database;
using Qizhu.Infrastructure.Identity;
using SqlSugar;

namespace Qizhu.Infrastructure.Tenancy;

/// <summary>租户列表及当前状态；不会向客户端公开其他租户的成员资料。</summary>
public sealed class TenantSummary
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
public sealed class MembershipContext
{
    public long MembershipId { get; set; }
    public long TenantId { get; set; }
    public long UserId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>租户授权边界；权限每次从当前成员关系读取，撤权不会因 Cookie 中的旧角色继续生效。</summary>
public interface ITenantDirectory
{
    Task<bool> IsAdministratorAsync(long userId, long tenantId);
    Task<List<TenantSummary>> ListAsync(long userId);
    /// <summary>写事务须使用 Serializable 隔离级别，保留租户、成员和授权读锁直到提交。</summary>
    Task<MembershipContext> RequireMembershipAsync(long userId, long tenantId, bool writing = false);
    Task<List<string>> GetPermissionsAsync(long userId, long tenantId);
}

public sealed class TenantDirectory(IDatabaseConnection connection, TimeProvider clock) : ITenantDirectory
{
    public async Task<bool> IsAdministratorAsync(long userId, long tenantId)
    {
        var platform = await connection.Client.Queryable<PlatformUserRoleRecord>().Where(link => link.UserId == userId
            && SqlFunc.Subqueryable<FrameworkRole>().Where(role => role.Id == link.RoleId && role.NormalizedName == "PLATFORM_ADMIN").Any()).AnyAsync();
        if (platform) { return true; }
        return await connection.Client.Queryable<MembershipRecord>()
            .InnerJoin<MembershipRoleRecord>((member, link) => member.TenantId == link.TenantId && member.Id == link.MembershipId)
            .InnerJoin<TenantRoleRecord>((member, link, role) => role.TenantId == link.TenantId && role.Id == link.RoleId)
            .Where((member, link, role) => member.UserId == userId && member.TenantId == tenantId && member.Status == "active" && role.Code == "tenant_admin").AnyAsync();
    }

    public async Task<List<TenantSummary>> ListAsync(long userId)
    {
        var now = clock.GetUtcNow();
        return await connection.Client.Queryable<TenantRecord>()
            .InnerJoin<MembershipRecord>((tenant, member) => tenant.Id == member.TenantId)
            .Where((tenant, member) => member.UserId == userId && member.Status == "active" && tenant.State != "deleted" && tenant.State != "deletion_pending")
            .OrderBy((tenant, member) => tenant.Name)
            .Select((tenant, member) => new TenantSummary
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Slug = tenant.Slug,
                State = (tenant.State == "active" || tenant.State == "trial") && tenant.ExpiresAt <= now ? "read_only" : tenant.State
            }).ToListAsync();
    }

    public async Task<MembershipContext> RequireMembershipAsync(long userId, long tenantId, bool writing = false)
    {
        var membership = await connection.Client.Queryable<TenantRecord>()
            .InnerJoin<MembershipRecord>((tenant, member) => tenant.Id == member.TenantId)
            .Where((tenant, member) => tenant.Id == tenantId && member.UserId == userId && member.Status == "active")
            .Select((tenant, member) => new MembershipContext { MembershipId = member.Id, TenantId = tenant.Id, UserId = member.UserId, State = tenant.State, ExpiresAt = tenant.ExpiresAt }).SingleAsync()
            ?? throw new ApiException(403, "tenant.membership_required", "你不是该租户的有效成员。");
        if (membership.State is not ("trial" or "active" or "read_only")) { throw new ApiException(403, "tenant.unavailable", "当前租户暂不可用。"); }
        if (writing && (membership.State == "read_only" || membership.ExpiresAt <= clock.GetUtcNow())) { throw new ApiException(403, "tenant.read_only", "当前租户为只读状态。"); }
        return membership;
    }

    public Task<List<string>> GetPermissionsAsync(long userId, long tenantId) => connection.Client.Queryable<MembershipRecord>()
        .InnerJoin<MembershipRoleRecord>((member, link) => member.TenantId == link.TenantId && member.Id == link.MembershipId)
        .InnerJoin<RolePermissionRecord>((member, link, permission) => permission.TenantId == link.TenantId && permission.RoleId == link.RoleId)
        .Where((member, link, permission) => member.TenantId == tenantId && member.UserId == userId && member.Status == "active")
        .Select((member, link, permission) => permission.PermissionCode).Distinct().ToListAsync();
}
