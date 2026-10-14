using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Infrastructure.Identity;
using Qizhu.Infrastructure.Tenancy;

namespace Qizhu.Api.Modules.Identity;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(UserManager<FrameworkUser> users, ISessionStore sessions, ITenantDirectory tenants,
    ICurrentActor actor, IHostEnvironment environment, ICredentialRateLimiter rateLimiter) : ControllerBase
{
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<FrameworkUser>(
        Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 220000 }))
        .HashPassword(new FrameworkUser(), "dummy-password-for-timing-only"));
    [HttpGet("csrf")]
    [AllowAnonymous]
    public object Csrf([FromServices] IAntiforgery antiforgery) => new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<object> Login(LoginRequest request)
    {
        if (request.RecoveryCode is not null && request.Code is not null)
        {
            throw new ApiException(400, "request.invalid", "动态验证码与恢复码不能同时提交。");
        }
        await rateLimiter.RequirePermitAsync(request.UserName, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var user = await users.FindByNameAsync(request.UserName);
        if (user is null || !user.IsEnabled || await users.IsLockedOutAsync(user))
        {
            _ = users.PasswordHasher.VerifyHashedPassword(new FrameworkUser(), DummyHash.Value, request.Password);
            throw new ApiException(401, "auth.invalid_credentials", "登录失败，请检查账号、密码或锁定状态。");
        }
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await rateLimiter.RecordFailureAsync(user.Id);
            throw new ApiException(401, "auth.invalid_credentials", "登录失败，请检查账号、密码或锁定状态。");
        }
        if (user.TwoFactorEnabled)
        {
            var verified = request.RecoveryCode is not null
                ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode)).Succeeded
                : request.Code is not null && await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
            if (!verified)
            {
                await rateLimiter.RecordFailureAsync(user.Id);
                throw new ApiException(401, "auth.mfa_required", "请输入有效的动态验证码或恢复码。");
            }
        }
        if (await users.IsLockedOutAsync(user))
        {
            throw new ApiException(401, "auth.mfa_required", "请输入有效的动态验证码。");
        }
        await users.ResetAccessFailedCountAsync(user);
        await SignInAsync(user, null, null, user.TwoFactorEnabled ? "mfa" : "pwd");
        return await ProfileAsync(user);
    }

    [HttpGet("me")]
    public async Task<object> Me() => await ProfileAsync(await CurrentUserAsync());

    [HttpGet("tenants")]
    public Task<List<TenantSummary>> Tenants() => tenants.ListAsync(actor.UserId);

    [HttpPost("tenant")]
    public async Task<object> SwitchTenant(SwitchTenantRequest request)
    {
        var user = await CurrentUserAsync();
        var member = await tenants.RequireMembershipAsync(user.Id, request.TenantId);
        await sessions.RevokeAsync(actor.SessionId, user.Id);
        await SignInAsync(user, member.TenantId, member.MembershipId, User.FindFirstValue("amr") ?? "pwd");
        return await ProfileAsync(user, member.TenantId);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await sessions.RevokeAsync(actor.SessionId, actor.UserId);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await CurrentUserAsync();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) { throw new ApiException(400, "request.invalid", "密码修改失败，请检查当前密码与新密码要求。"); }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpPost("mfa/setup")]
    public async Task<object> SetupMfa()
    {
        var user = await CurrentUserAsync();
        if (user.TwoFactorEnabled) { throw new ApiException(409, "business.state_conflict", "已启用多因素认证，不能直接重置。"); }
        var oldSession = await sessions.ValidateAsync(actor.SessionId, user.Id);
        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("认证密钥创建失败。");
        await sessions.RevokeAsync(actor.SessionId, user.Id);
        await SignInAsync(user, oldSession?.TenantId, oldSession?.MembershipId, "pwd");
        return new { key, uri = $"otpauth://totp/Qizhu:{Uri.EscapeDataString(user.UserName!)}?secret={key}&issuer=Qizhu&digits=6" };
    }

    [HttpPost("mfa/enable")]
    public async Task<object> EnableMfa(MfaCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code))
        {
            throw new ApiException(400, "request.invalid", "动态验证码不正确。");
        }
        var session = await sessions.ValidateAsync(actor.SessionId, user.Id);
        await users.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await sessions.RevokeAsync(actor.SessionId, user.Id);
        await SignInAsync(user, session?.TenantId, session?.MembershipId, "mfa");
        return new { recoveryCodes };
    }

    private async Task<FrameworkUser> CurrentUserAsync() => await users.FindByIdAsync(actor.UserId.ToString())
        ?? throw new ApiException(401, "auth.session_expired", "账号已不可用。");

    private async Task<object> ProfileAsync(FrameworkUser user, long? selectedTenant = null)
    {
        var available = await tenants.ListAsync(user.Id);
        var tenantId = selectedTenant ?? (long.TryParse(User.FindFirstValue("tenant_id"), out var id) ? id : (long?)null);
        var permissions = tenantId is { } selected ? await tenants.GetPermissionsAsync(user.Id, selected) : [];
        var administrator = await users.IsInRoleAsync(user, "platform_admin") || (tenantId is { } current && await tenants.IsAdministratorAsync(user.Id, current));
        return new
        {
            user.Id,
            user.UserName,
            user.DisplayName,
            user.TwoFactorEnabled,
            TenantId = tenantId,
            Tenants = available,
            Permissions = permissions,
            MfaSetupRequired = !environment.IsDevelopment() && administrator && !user.TwoFactorEnabled
        };
    }

    private async Task SignInAsync(FrameworkUser user, long? tenantId, long? membershipId, string method)
    {
        var session = await sessions.CreateAsync(user, tenantId, membershipId);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.UserName!),
            new("session_id", session.Id.ToString()), new("amr", method) };
        if (tenantId is { } tenant) { claims.Add(new Claim("tenant_id", tenant.ToString())); }
        if (membershipId is { } member) { claims.Add(new Claim("membership_id", member.ToString())); }
        foreach (var role in await users.GetRolesAsync(user)) { claims.Add(new Claim(ClaimTypes.Role, role)); }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
    }
}

public sealed class LoginRequest
{
    [Required, StringLength(64)] public string UserName { get; init; } = string.Empty;
    [Required, StringLength(128)] public string Password { get; init; } = string.Empty;
    [RegularExpression("^[0-9]{6}$")] public string? Code { get; init; }
    [StringLength(128)] public string? RecoveryCode { get; init; }
}
public sealed record SwitchTenantRequest(long TenantId);
public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)] public string CurrentPassword { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 15)] public string NewPassword { get; init; } = string.Empty;
}
public sealed class MfaCodeRequest
{
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = string.Empty;
}
