using System.Diagnostics;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Qizhu.Api.Configuration;
using Qizhu.Api.Errors;
using Qizhu.Api.Health;
using Qizhu.Api.Modules;
using Qizhu.Api.Observability;
using Qizhu.Api.Security;
using Qizhu.Api.Serialization;
using Qizhu.Api.Tenancy;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Projects;
using Qizhu.Infrastructure.Auditing;
using Qizhu.Infrastructure.Common;
using Qizhu.Infrastructure.Database;
using Qizhu.Infrastructure.Identity;
using Qizhu.Infrastructure.Projects;
using Qizhu.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => { options.UseUtcTimestamp = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ"; options.IncludeScopes = true; });
if (builder.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("QIZHU_IGNORE_LOCAL_CONFIGURATION") != "1"
    && builder.Configuration.GetValue("LoadLocalConfiguration", true))
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables().AddCommandLine(args);
}
var sqlEnabled = builder.Configuration["Database:Provider"] == "SqlServer";
var settings = builder.Configuration.GetSection(FrameworkOptions.SectionName).Get<FrameworkOptions>() ?? new();
builder.Services.AddOptions<FrameworkOptions>().BindConfiguration(FrameworkOptions.SectionName).ValidateDataAnnotations()
    .Validate(options => Enum.IsDefined(options.DeploymentMode), "部署模式无效。")
    .Validate(options => options.DeploymentMode != DeploymentMode.SingleTenant || options.DefaultTenantId > 0, "独立部署必须配置默认租户。")
    .Validate(options => !sqlEnabled || options.DeploymentMode != DeploymentMode.SingleTenant || options.DefaultTenantId > 1
        || builder.Configuration.GetValue<bool>("Database:Initialize"), "SQL Server 独立部署必须配置初始化返回的雪花租户 ID。")
    .Validate(options => !options.EnableDevelopmentEndpoints || builder.Environment.IsDevelopment(), "演示接口只能在 Development 环境开启。")
    .Validate(options => !sqlEnabled || !options.EnableDevelopmentEndpoints, "SQL Server 模式不开放匿名演示接口。")
    .ValidateOnStart();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
    {
        401 => "auth.unauthenticated",
        403 => "auth.forbidden",
        404 => "resource.not_found",
        413 => "request.body_too_large",
        429 => "rate_limit.exceeded",
        >= 500 => "server.error",
        _ => "request.invalid"
    });
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new LongStringJsonConverter())).ConfigureApplicationPartManager(manager =>
    manager.FeatureProviders.Add(new DevelopmentControllerFeatureProvider(builder.Environment.IsDevelopment() && settings.EnableDevelopmentEndpoints, sqlEnabled)))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = 400,
            Title = "请检查输入内容",
            Instance = context.HttpContext.Request.Path,
            Extensions = { ["code"] = "request.invalid", ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier,
                ["requestId"] = context.HttpContext.TraceIdentifier }
        };
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    });
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddScoped<ProjectService>();
var protection = builder.Services.AddDataProtection().SetApplicationName($"Qizhu:{builder.Environment.EnvironmentName}");
if (sqlEnabled)
{
    var connectionString = builder.Configuration.GetConnectionString("Main")
        ?? throw new InvalidOperationException("SQL Server 模式需要配置 ConnectionStrings:Main。");
    builder.Services.AddScoped<IDatabaseConnection>(provider =>
    {
        var connection = new DatabaseConnection(connectionString);
        connection.Client.Ado.CancellationToken = provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.RequestAborted;
        return connection;
    });
    builder.Services.AddSingleton<ISnowflakeStateStore>(new SqlSugarSnowflakeStateStore(connectionString));
    builder.Services.AddScoped<IProjectRepository, SqlSugarProjectRepository>();
    builder.Services.AddScoped<IProjectWorkspace, TransactionalProjectWorkspace>();
    builder.Services.AddScoped<ITenantDirectory, TenantDirectory>();
    builder.Services.AddScoped<ISessionStore, SessionStore>();
    builder.Services.AddScoped<ICredentialRateLimiter, CredentialRateLimiter>();
    builder.Services.AddScoped<IAuditWriter, AuditWriter>();
    builder.Services.AddScoped<IDatabaseBootstrapper, DatabaseBootstrapper>();
    builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("sql_server", tags: ["ready"]);
    builder.Services.AddIdentityCore<FrameworkUser>(options =>
    {
        options.Password.RequiredLength = 15;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }).AddRoles<FrameworkRole>().AddUserStore<SqlSugarUserStore>().AddRoleStore<SqlSugarRoleStore>()
        .AddPasswordValidator<FrameworkPasswordValidator>().AddDefaultTokenProviders();
    builder.Services.AddScoped<ILookupNormalizer, FrameworkLookupNormalizer>();
    builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 220000);
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "Qizhu.Antiforgery";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
    var keyPath = builder.Configuration["Security:KeyRingPath"];
    if (string.IsNullOrEmpty(keyPath) && builder.Environment.IsDevelopment())
    {
        keyPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../work/data-protection"));
    }
    if (string.IsNullOrEmpty(keyPath)) { throw new InvalidOperationException("正式环境必须配置受保护的 Security:KeyRingPath。"); }
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (OperatingSystem.IsWindows() && builder.Environment.IsDevelopment()) { protection.ProtectKeysWithDpapi(); }
    else if (!builder.Environment.IsDevelopment())
    {
        protection.ProtectKeysWithCertificate(builder.Configuration["Security:KeyEncryptionCertificate"]
            ?? throw new InvalidOperationException("正式环境必须配置可供全部实例读取的密钥加密证书。"));
    }
}
else
{
    builder.Services.AddSingleton<ISnowflakeStateStore, InMemorySnowflakeStateStore>();
    builder.Services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
}
builder.Services.AddSingleton<IIdGenerator>(provider => new SnowflakeIdGenerator(provider.GetRequiredService<TimeProvider>(),
    provider.GetRequiredService<ISnowflakeStateStore>(), builder.Configuration.GetValue("Snowflake:WorkerId", 1)));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "Qizhu.Session";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    if (sqlEnabled)
    {
        options.Events.OnValidatePrincipal = async context =>
        {
            if (!long.TryParse(context.Principal?.FindFirstValue("session_id"), out var sessionId) || sessionId <= 0
                || !long.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0
                || await context.HttpContext.RequestServices.GetRequiredService<ISessionStore>().ValidateAsync(sessionId, userId) is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    }
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:Initialize"))
{
    try
    {
        _ = app.Services.GetRequiredService<IOptions<FrameworkOptions>>().Value;
        DatabaseMigrator.Upgrade(builder.Configuration.GetConnectionString("Main") ?? throw new InvalidOperationException("未配置数据库连接。"));
        if (builder.Configuration.GetValue<bool>("Bootstrap:Enabled"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var initializedTenantId = await scope.ServiceProvider.GetRequiredService<IDatabaseBootstrapper>().InitializeAsync(
                builder.Configuration["Bootstrap:UserName"] ?? "admin",
                builder.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("未配置初始管理员密码。"), settings.DefaultTenantId);
            app.Logger.LogInformation("初始租户 ID：{TenantId}；独立部署请将其配置为 Framework:DefaultTenantId。", initializedTenantId);
        }
        app.Logger.LogInformation("数据库结构与初始数据检查完成。");
    }
    catch (Exception exception)
    {
        app.Logger.LogError("数据库初始化未完成：{ErrorType}。未输出连接信息或密码。", exception.GetType().Name);
        Environment.ExitCode = 1;
    }
    return;
}
app.UseForwardedHeaders();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (sqlEnabled) { app.UseMiddleware<AntiforgeryValidationMiddleware>(); }
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
if (sqlEnabled) { app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous(); }
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapGet("/", () => Results.Ok(new { name = settings.ApplicationName, documentation = "/openapi/v1.json", health = "/health/live", database = sqlEnabled ? "SqlServer" : "Memory" })).AllowAnonymous();
}
app.Run();
public partial class Program;
