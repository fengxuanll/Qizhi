using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qizhu.Api.Configuration;
using Qizhu.Api.Tenancy;
using Qizhu.Domain.Common;

namespace Qizhu.Tests.Api;

public sealed class TenantIdentityTests
{
    [Fact]
    public void AuthenticatedIdentityTakesPrecedenceOverDevelopmentHeader()
    {
        var tenantId = TestIds.Next();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "test"))
        };
        context.Request.Headers["X-Tenant-Id"] = TestIds.Next().ToString();
        Assert.Equal(tenantId, CreateTenant(context, development: true).Id);
    }

    [Fact]
    public void AuthenticatedIdentityWithoutTenantCannotUseHeaderAsFallback()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "test")) };
        context.Request.Headers["X-Tenant-Id"] = TestIds.Next().ToString();
        Assert.Throws<BusinessRuleException>(() => CreateTenant(context, development: true).Id);
    }

    [Fact]
    public void ProductionNeverAcceptsAnonymousTenantHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = TestIds.Next().ToString();
        Assert.Throws<BusinessRuleException>(() => CreateTenant(context, development: false).Id);
    }

    [Fact]
    public void ProductionAcceptsTenantFromAuthenticatedIdentity()
    {
        var tenantId = TestIds.Next();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "test"))
        };
        Assert.Equal(tenantId, CreateTenant(context, development: false).Id);
    }

    private static HttpCurrentTenant CreateTenant(HttpContext context, bool development) => new(
        new HttpContextAccessor { HttpContext = context },
        Options.Create(new FrameworkOptions { DeploymentMode = DeploymentMode.MultiTenant, EnableDevelopmentEndpoints = true }),
        new TestEnvironment(development ? Environments.Development : Environments.Production));

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Qizhu.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
