using Microsoft.Extensions.Options;
using Qizhu.Api.Configuration;
using Qizhu.Application.Abstractions;
using Qizhu.Domain.Common;

namespace Qizhu.Api.Tenancy;

public sealed class HttpCurrentTenant(
    IHttpContextAccessor accessor,
    IOptions<FrameworkOptions> options,
    IHostEnvironment environment) : ICurrentTenant
{
    public long Id
    {
        get
        {
            var settings = options.Value;
            if (settings.DeploymentMode == DeploymentMode.SingleTenant)
            {
                return settings.DefaultTenantId;
            }

            var context = accessor.HttpContext ?? throw new InvalidOperationException("当前没有 HTTP 请求。");
            var tenantValue = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("tenant_id")?.Value
                : environment.IsDevelopment() && settings.EnableDevelopmentEndpoints
                    ? context.Request.Headers["X-Tenant-Id"].ToString()
                    : null;

            if (!long.TryParse(tenantValue, out var tenantId) || tenantId <= 0)
            {
                throw new BusinessRuleException("tenant.invalid", "缺少有效的租户身份。");
            }

            return tenantId;
        }
    }
}
