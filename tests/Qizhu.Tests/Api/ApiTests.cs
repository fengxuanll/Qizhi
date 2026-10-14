using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;

namespace Qizhu.Tests.Api;

public sealed class ApiTests
{
    [Fact]
    public async Task CreateReturnsLocationAndCanBeRead()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "  示例项目  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var project = await client.GetFromJsonAsync<ProjectDto>(response.Headers.Location);
        Assert.Equal("示例项目", project!.Name);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }

    [Theory]
    [InlineData("{\"name\":\"   \"}")]
    [InlineData("{}")]
    [InlineData("{broken")]
    public async Task InvalidBodyReturnsProblemDetails(string body)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/projects", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task InvalidPaginationReturnsStableBusinessErrorCode()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/projects?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pagination.invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MultiTenantModeIsolatesDetailAndList()
    {
        using var factory = CreateFactory(multiTenant: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestIds.Next().ToString());
        var created = await client.PostAsJsonAsync("/api/v1/projects", new { name = "租户 A" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestIds.Next().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(created.Headers.Location)).StatusCode);
        var page = await client.GetFromJsonAsync<PagedResult<ProjectDto>>("/api/v1/projects");
        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MultiTenantModeRejectsMissingOrInvalidTenant(string? tenant)
    {
        using var factory = CreateFactory(multiTenant: true);
        using var client = factory.CreateClient();
        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task SingleTenantModeIgnoresClientTenantHeader()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "独立部署" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestIds.Next().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task DevelopmentPublishesOpenApiDocument()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/projects", out _));
    }

    [Fact]
    public async Task ProductionExposesHealthButProtectsOtherRoutes()
    {
        using var factory = CreateFactory(production: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        Assert.DoesNotContain(actions, action => action.DisplayName?.Contains("ProjectsController", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProductionRejectsEnabledDevelopmentEndpoints()
    {
        using var factory = CreateFactory(production: true, enableDevelopmentEndpoints: true);
        var exception = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("演示接口只能", exception.Message);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool production = false, bool multiTenant = false,
        bool? enableDevelopmentEndpoints = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(production ? "Production" : "Development");
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LoadLocalConfiguration"] = "false",
                ["Database:Provider"] = "Memory",
                ["Framework:DeploymentMode"] = multiTenant ? "MultiTenant" : "SingleTenant",
                ["Framework:EnableDevelopmentEndpoints"] = (enableDevelopmentEndpoints ?? !production).ToString()
            }));
        });
}
