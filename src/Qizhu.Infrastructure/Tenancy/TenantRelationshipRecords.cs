using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Tenancy;

/// <summary>全局账号在租户内的身份；用户、租户组合唯一。</summary>
[SugarTable("dbo.tenancy_tenant_memberships")]
public sealed class MembershipRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    [SugarColumn(ColumnName = "user_id")] public long UserId { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "display_name")] public string DisplayName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>租户角色，租户和角色码组合唯一。</summary>
[SugarTable("dbo.tenancy_tenant_roles")]
public sealed class TenantRoleRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    [SugarColumn(ColumnName = "code")] public string Code { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
}

/// <summary>成员角色关联；复合外键保证角色和成员属于同一租户。</summary>
[SugarTable("dbo.tenancy_membership_roles")]
public sealed class MembershipRoleRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    [SugarColumn(ColumnName = "membership_id")] public long MembershipId { get; set; }
    [SugarColumn(ColumnName = "role_id")] public long RoleId { get; set; }
}

/// <summary>租户角色与权限码的关联，不能跨租户授权。</summary>
[SugarTable("dbo.tenancy_role_permissions")]
public sealed class RolePermissionRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    [SugarColumn(ColumnName = "role_id")] public long RoleId { get; set; }
    [SugarColumn(ColumnName = "permission_code")] public string PermissionCode { get; set; } = string.Empty;
}

/// <summary>权限业务码保持唯一稳定；主键仍为雪花 long。</summary>
[SugarTable("dbo.system_permissions")]
public sealed class PermissionRecord : EntityRecord
{
    [SugarColumn(ColumnName = "code")] public string Code { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scope")] public string Scope { get; set; } = string.Empty;
}
