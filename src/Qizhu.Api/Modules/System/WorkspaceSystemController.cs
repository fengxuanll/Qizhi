using Microsoft.AspNetCore.Mvc;
using Qizhu.Api.Security;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Infrastructure.Auditing;
using Qizhu.Infrastructure.Database;
using Qizhu.Infrastructure.Tenancy;
using SqlSugar;

namespace Qizhu.Api.Modules.System;

/// <summary>系统只读查询；列表必须显式限定当前租户。</summary>
[ApiController]
[Route("api/v1/system")]
public sealed class WorkspaceSystemController(IDatabaseConnection connection, ICurrentTenant tenant) : ControllerBase
{
    [HttpGet("modules")]
    [RequireTenantPermission("system.modules.read")]
    public object Modules() => new[]
    {
        new { Code = "identity", Name = "身份与账号", Description = "全局账号、会话与多因素认证", Core = true },
        new { Code = "tenancy", Name = "租户与成员", Description = "成员身份校验与租户权限隔离", Core = true },
        new { Code = "system", Name = "系统基础", Description = "接口规范、审计与健康检查", Core = true },
        new { Code = "projects", Name = "项目管理", Description = "持久化项目、版本校验、幂等与回收站", Core = false }
    };

    [HttpGet("audit")]
    [RequireTenantPermission("system.audit.read")]
    public async Task<PagedResult<AuditItem>> Audit([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100 || ((long)pageNumber - 1) * pageSize > 10000) { throw new ApiException(400, "pagination.invalid", "请检查审计页码与每页数量。"); }
        var total = new RefAsync<int>();
        var items = await connection.Client.Queryable<AuditRecord>()
            .LeftJoin<MembershipRecord>((audit, member) => member.TenantId == audit.TenantId && member.UserId == audit.ActorId)
            .Where((audit, member) => audit.TenantId == tenant.Id)
            .OrderBy((audit, member) => audit.OccurredAt, OrderByType.Desc).OrderBy((audit, member) => audit.Id, OrderByType.Desc)
            .Select((audit, member) => new AuditItem
            {
                Id = audit.Id,
                Action = audit.Action,
                Summary = audit.Summary,
                ResourceId = audit.ResourceId,
                OccurredAt = audit.OccurredAt,
                TraceId = audit.TraceId,
                ActorName = member.DisplayName ?? "系统"
            })
            .ToPageListAsync(pageNumber, pageSize, total);
        return new PagedResult<AuditItem>(items, total.Value, pageNumber, pageSize);
    }
}

public sealed class AuditItem
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public long? ResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceId { get; set; }
    public string ActorName { get; set; } = string.Empty;
}
