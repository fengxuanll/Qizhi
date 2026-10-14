using SqlSugar;

namespace Qizhu.Infrastructure.Database;

/// <summary>普通持久化实体的主键基类。ID 由 IIdGenerator 在应用/仓储边界分配，禁止数据库自增。</summary>
public abstract class EntityRecord
{
    /// <summary>正数雪花主键；JSON 中以字符串输出，SQL Server 中为 bigint。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public long Id { get; set; }
}

/// <summary>拥有创建、更新和软删除审计的租户实体；更新必须显式带租户与版本条件。</summary>
public abstract class AuditedTenantRecord : EntityRecord
{
    [SugarColumn(ColumnName = "tenant_id")] public long TenantId { get; set; }
    /// <summary>所有时间字段是 UTC datetimeoffset(7)；数据库 CHECK 约束偏移量为零。</summary>
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset CreatedAt { get; set; }
    [SugarColumn(ColumnName = "created_by", IsNullable = true)] public long? CreatedBy { get; set; }
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset UpdatedAt { get; set; }
    [SugarColumn(ColumnName = "updated_by", IsNullable = true)] public long? UpdatedBy { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "deleted_at", ColumnDataType = "datetimeoffset(7)", IsNullable = true)] public DateTimeOffset? DeletedAt { get; set; }
    [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public long? DeletedBy { get; set; }
    /// <summary>数据库生成的 rowversion，不能作为业务时间；客户端通过强 ETag/If-Match 回传。</summary>
    [SugarColumn(ColumnName = "row_version", IsOnlyIgnoreInsert = true, IsOnlyIgnoreUpdate = true)] public byte[] RowVersion { get; set; } = [];
}
