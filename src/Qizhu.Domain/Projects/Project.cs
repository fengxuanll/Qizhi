using System.Text;
using Qizhu.Domain.Common;

namespace Qizhu.Domain.Projects;

/// <summary>示例业务实体。保持不可变，避免调用方绕过规则修改状态。</summary>
public sealed record Project
{
    public const int MaximumNameLength = 100;

    private Project(long id, long tenantId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public long Id { get; }
    public long TenantId { get; }
    public string Name { get; }
    public DateTimeOffset CreatedAt { get; }

    public static Project Restore(long id, long tenantId, string name, DateTimeOffset createdAt)
    {
        if (id <= 0 || tenantId <= 0 || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("持久化项目数据不完整。");
        }
        return new Project(id, tenantId, name, createdAt);
    }

    public static Project Create(long id, long tenantId, string? name, DateTimeOffset createdAt)
    {
        if (tenantId <= 0)
        {
            throw new BusinessRuleException("tenant.required", "必须指定所属租户。");
        }

        if (id <= 0) { throw new BusinessRuleException("entity.id_invalid", "主键必须是正数雪花 ID。"); }
        return new Project(id, tenantId, NormalizeName(name), createdAt);
    }

    /// <summary>新增和修改共用名称规则，不因校验名称分配额外主键。</summary>
    public static string NormalizeName(string? name)
    {
        var cleanName = name?.Trim().Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new BusinessRuleException("project.name_required", "请输入项目名称。");
        }

        if (cleanName.Length > MaximumNameLength)
        {
            throw new BusinessRuleException("project.name_too_long", $"项目名称不能超过 {MaximumNameLength} 个字符。");
        }

        return cleanName;
    }
}
