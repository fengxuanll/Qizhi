using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Projects;

/// <summary>项目的 SqlSugar 映射；业务规则在 Domain.Project，持久化公共字段在审计基类。</summary>
[SugarTable("dbo.projects")]
public sealed class ProjectRecord : AuditedTenantRecord
{
    [SugarColumn(ColumnName = "name", Length = 100)] public string Name { get; set; } = string.Empty;
}
