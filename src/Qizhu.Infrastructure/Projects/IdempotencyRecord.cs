using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Projects;

/// <summary>幂等结果与业务写入在同一事务保存；唯一范围为租户、操作者、操作和键哈希。</summary>
[SugarTable("dbo.system_idempotency_records")]
public sealed class IdempotencyRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    [SugarColumn(ColumnName = "actor_id")] public long ActorId { get; set; }
    [SugarColumn(ColumnName = "operation")] public string Operation { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "key_hash")] public byte[] KeyHash { get; set; } = [];
    [SugarColumn(ColumnName = "request_hash")] public byte[] RequestHash { get; set; } = [];
    [SugarColumn(ColumnName = "status_code")] public int StatusCode { get; set; }
    [SugarColumn(ColumnName = "response_json")] public string ResponseJson { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "location", IsNullable = true)] public string? Location { get; set; }
    [SugarColumn(ColumnName = "etag", IsNullable = true)] public string? Etag { get; set; }
    [SugarColumn(ColumnName = "expires_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset ExpiresAt { get; set; }
}
