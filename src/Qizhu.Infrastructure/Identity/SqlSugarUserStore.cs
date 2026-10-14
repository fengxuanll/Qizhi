using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Qizhu.Application.Abstractions;
using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

/// <summary>将 Identity 能力映射为实体操作；账号全局唯一，租户角色由独立成员权限服务维护。</summary>
public sealed class SqlSugarUserStore(IDatabaseConnection connection, IDataProtectionProvider protection, IIdGenerator ids, TimeProvider clock) :
    IUserPasswordStore<FrameworkUser>, IUserSecurityStampStore<FrameworkUser>, IUserLockoutStore<FrameworkUser>,
    IUserEmailStore<FrameworkUser>, IUserTwoFactorStore<FrameworkUser>, IUserAuthenticatorKeyStore<FrameworkUser>,
    IUserTwoFactorRecoveryCodeStore<FrameworkUser>, IUserRoleStore<FrameworkUser>
{
    private readonly IDataProtector _protector = protection.CreateProtector("Qizhu.Identity.Authenticator.v1");
    public void Dispose() { }
    public Task<string> GetUserIdAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
    public Task<string?> GetUserNameAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(FrameworkUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(FrameworkUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
    public async Task<FrameworkUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkUser>().Where(user => user.Id == long.Parse(userId)).SingleAsync();
    public async Task<FrameworkUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkUser>().Where(user => user.NormalizedUserName == normalizedUserName).SingleAsync();
    public async Task<IdentityResult> CreateAsync(FrameworkUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user.Id == 0) { user.Id = ids.NextId(); }
        user.CreatedAt = clock.GetUtcNow();
        user.UpdatedAt = user.CreatedAt;
        user.LockoutEnd = user.LockoutEnd?.ToUniversalTime();
        await connection.Client.Insertable(user).ExecuteCommandAsync();
        return IdentityResult.Success;
    }
    public async Task<IdentityResult> UpdateAsync(FrameworkUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previousStamp = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = clock.GetUtcNow();
        user.LockoutEnd = user.LockoutEnd?.ToUniversalTime();
        var count = await connection.Client.Updateable(user).Where(item => item.Id == user.Id && item.ConcurrencyStamp == previousStamp).ExecuteCommandAsync();
        return count == 1 ? IdentityResult.Success : IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    }
    public Task<IdentityResult> DeleteAsync(FrameworkUser user, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "UserDeletionRequiresWorkflow", Description = "账号注销必须经过留存与成员清理流程。" }));
    public Task SetPasswordHashAsync(FrameworkUser user, string? hash, CancellationToken cancellationToken) { user.PasswordHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);
    public Task SetSecurityStampAsync(FrameworkUser user, string stamp, CancellationToken cancellationToken) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(FrameworkUser user, DateTimeOffset? end, CancellationToken cancellationToken) { user.LockoutEnd = end?.ToUniversalTime(); return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(FrameworkUser user, CancellationToken cancellationToken) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(FrameworkUser user, bool enabled, CancellationToken cancellationToken) { user.LockoutEnabled = enabled; return Task.CompletedTask; }
    public Task SetEmailAsync(FrameworkUser user, string? email, CancellationToken cancellationToken) { user.Email = email; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
    public Task<bool> GetEmailConfirmedAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
    public Task SetEmailConfirmedAsync(FrameworkUser user, bool confirmed, CancellationToken cancellationToken) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
    public async Task<FrameworkUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkUser>().Where(user => user.NormalizedEmail == normalizedEmail && user.EmailConfirmed).SingleAsync();
    public Task<string?> GetNormalizedEmailAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(FrameworkUser user, string? email, CancellationToken cancellationToken) { user.NormalizedEmail = email; return Task.CompletedTask; }
    public Task SetTwoFactorEnabledAsync(FrameworkUser user, bool enabled, CancellationToken cancellationToken) { user.TwoFactorEnabled = enabled; return Task.CompletedTask; }
    public Task<bool> GetTwoFactorEnabledAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult(user.TwoFactorEnabled);
    public Task SetAuthenticatorKeyAsync(FrameworkUser user, string key, CancellationToken cancellationToken) { user.ProtectedAuthenticatorKey = _protector.Protect(key); return Task.CompletedTask; }
    public Task<string?> GetAuthenticatorKeyAsync(FrameworkUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.ProtectedAuthenticatorKey is null ? null : _protector.Unprotect(user.ProtectedAuthenticatorKey));
    public Task ReplaceCodesAsync(FrameworkUser user, IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        user.RecoveryCodes = string.Join(';', codes.Select(HashRecoveryCode));
        return Task.CompletedTask;
    }
    public Task<bool> RedeemCodeAsync(FrameworkUser user, string code, CancellationToken cancellationToken)
    {
        var codes = (user.RecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hash = HashRecoveryCode(code);
        var found = codes.Remove(hash);
        if (found) { user.RecoveryCodes = string.Join(';', codes); }
        return Task.FromResult(found);
    }
    public Task<int> CountCodesAsync(FrameworkUser user, CancellationToken cancellationToken) => Task.FromResult((user.RecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
    public async Task AddToRoleAsync(FrameworkUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await connection.Client.Queryable<FrameworkRole>().Where(item => item.NormalizedName == roleName).SingleAsync()
            ?? throw new InvalidOperationException("平台角色不存在。");
        await connection.Client.Insertable(new PlatformUserRoleRecord { Id = ids.NextId(), UserId = user.Id, RoleId = role.Id }).ExecuteCommandAsync();
    }
    public Task RemoveFromRoleAsync(FrameworkUser user, string roleName, CancellationToken cancellationToken) =>
        connection.Client.Deleteable<PlatformUserRoleRecord>().Where(link => link.UserId == user.Id
            && SqlFunc.Subqueryable<FrameworkRole>().Where(role => role.Id == link.RoleId && role.NormalizedName == roleName).Any()).ExecuteCommandAsync();
    public async Task<IList<string>> GetRolesAsync(FrameworkUser user, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkRole>().Where(role => SqlFunc.Subqueryable<PlatformUserRoleRecord>()
            .Where(link => link.RoleId == role.Id && link.UserId == user.Id).Any()).Select(role => role.Name!).ToListAsync();
    public Task<bool> IsInRoleAsync(FrameworkUser user, string roleName, CancellationToken cancellationToken) =>
        connection.Client.Queryable<PlatformUserRoleRecord>().Where(link => link.UserId == user.Id
            && SqlFunc.Subqueryable<FrameworkRole>().Where(role => role.Id == link.RoleId && role.NormalizedName == roleName).Any()).AnyAsync();
    public async Task<IList<FrameworkUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        await connection.Client.Queryable<FrameworkUser>().Where(user => SqlFunc.Subqueryable<PlatformUserRoleRecord>()
            .Where(link => link.UserId == user.Id && SqlFunc.Subqueryable<FrameworkRole>().Where(role => role.Id == link.RoleId && role.NormalizedName == roleName).Any()).Any()).ToListAsync();
    private static string HashRecoveryCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}

[SugarTable("dbo.identity_platform_user_roles")]
public sealed class PlatformUserRoleRecord : EntityRecord
{
    [SugarColumn(ColumnName = "user_id")] public long UserId { get; set; }
    [SugarColumn(ColumnName = "role_id")] public long RoleId { get; set; }
}
