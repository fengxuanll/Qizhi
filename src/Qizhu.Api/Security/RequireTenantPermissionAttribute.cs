using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Infrastructure.Tenancy;

namespace Qizhu.Api.Security;

public sealed class RequireTenantPermissionAttribute : TypeFilterAttribute
{
    public RequireTenantPermissionAttribute(string permission) : base(typeof(TenantPermissionFilter)) => Arguments = [permission];
}

public sealed class TenantPermissionFilter(string permission, ICurrentActor actor, ICurrentTenant tenant, ITenantDirectory tenants,
    IHostEnvironment environment) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) { throw new ApiException(401, "auth.unauthenticated", "请先登录。"); }
        if (!environment.IsDevelopment() && context.HttpContext.User.FindFirst("amr")?.Value != "mfa" && await tenants.IsAdministratorAsync(actor.UserId, tenant.Id))
        {
            throw new ApiException(403, "auth.mfa_required", "正式环境的管理操作需要多因素认证。");
        }
        var header = context.HttpContext.Request.Headers["X-Tenant-Context"].ToString();
        if (string.IsNullOrEmpty(header)) { throw new ApiException(400, "tenant.context_required", "请刷新页面以选择租户。"); }
        if (!long.TryParse(header, out var expected) || expected != tenant.Id)
        {
            throw new ApiException(409, "tenant.context_mismatch", "租户已切换，请刷新当前页面。");
        }
        await tenants.RequireMembershipAsync(actor.UserId, tenant.Id);
        var permissions = await tenants.GetPermissionsAsync(actor.UserId, tenant.Id);
        if (!permissions.Contains(permission)) { throw new ApiException(403, "auth.forbidden", "你没有执行该操作的权限。"); }
    }
}
