using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Auditing;

/// <summary>不可变审计事件；不记录密码、令牌或完整业务载荷。</summary>
[SugarTable("dbo.audit_events")]
public sealed class AuditRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id", IsNullable = true)] public long? TenantId { get; set; }
    [SugarColumn(ColumnName = "actor_id", IsNullable = true)] public long? ActorId { get; set; }
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "resource_id", IsNullable = true)] public long? ResourceId { get; set; }
    [SugarColumn(ColumnName = "summary")] public string Summary { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "occurred_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset OccurredAt { get; set; }
    [SugarColumn(ColumnName = "request_id", IsNullable = true)] public string? RequestId { get; set; }
    [SugarColumn(ColumnName = "trace_id", IsNullable = true)] public string? TraceId { get; set; }
    [SugarColumn(ColumnName = "actor_type")] public string ActorType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "resource_type")] public string ResourceType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "result")] public string Result { get; set; } = string.Empty;
}
