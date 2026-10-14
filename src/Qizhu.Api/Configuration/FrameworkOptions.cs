using System.ComponentModel.DataAnnotations;

namespace Qizhu.Api.Configuration;

public sealed class FrameworkOptions
{
    public const string SectionName = "Framework";

    [Required]
    public string ApplicationName { get; init; } = "Qizhu";

    public DeploymentMode DeploymentMode { get; init; } = DeploymentMode.SingleTenant;
    public long DefaultTenantId { get; init; } = 1L;

    /// <summary>仅开发环境可启用：开放匿名示例接口和模拟租户请求头。</summary>
    public bool EnableDevelopmentEndpoints { get; init; }
}

public enum DeploymentMode
{
    SingleTenant,
    MultiTenant
}
