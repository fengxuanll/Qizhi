using SqlSugar;

namespace Qizhu.Infrastructure.Tenancy;

[SugarTable("dbo.tenancy_tenants")]
public sealed class TenantRecord
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(ColumnName = "slug")] public string Slug { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "active";
    [SugarColumn(ColumnName = "expires_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset ExpiresAt { get; set; }
    [SugarColumn(ColumnName = "state_version")] public long StateVersion { get; set; } = 1;
    [SugarColumn(ColumnName = "authorization_version")] public long AuthorizationVersion { get; set; } = 1;
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset CreatedAt { get; set; }
}
