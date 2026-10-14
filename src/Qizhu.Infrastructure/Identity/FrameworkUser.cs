using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

/// <summary>全局账号，租户资料另存于成员关系中。</summary>
[SugarTable("dbo.identity_users")]
public sealed class FrameworkUser : IdentityUser<long>
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public override long Id { get; set; }
    [SugarColumn(ColumnName = "user_name")] public override string? UserName { get; set; }
    [SugarColumn(ColumnName = "normalized_user_name")] public override string? NormalizedUserName { get; set; }
    [SugarColumn(ColumnName = "password_hash", IsNullable = true)] public override string? PasswordHash { get; set; }
    [SugarColumn(ColumnName = "security_stamp")] public override string? SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(ColumnName = "concurrency_stamp")] public override string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(ColumnName = "email", IsNullable = true)] public override string? Email { get; set; }
    [SugarColumn(ColumnName = "normalized_email", IsNullable = true)] public override string? NormalizedEmail { get; set; }
    [SugarColumn(ColumnName = "email_confirmed")] public override bool EmailConfirmed { get; set; }
    [SugarColumn(ColumnName = "two_factor_enabled")] public override bool TwoFactorEnabled { get; set; }
    [SugarColumn(ColumnName = "access_failed_count")] public override int AccessFailedCount { get; set; }
    [SugarColumn(ColumnName = "lockout_enabled")] public override bool LockoutEnabled { get; set; } = true;
    [SugarColumn(ColumnName = "lockout_end", ColumnDataType = "datetimeoffset(7)", IsNullable = true)] public override DateTimeOffset? LockoutEnd { get; set; }
    [SugarColumn(IsIgnore = true)] public override string? PhoneNumber { get; set; }
    [SugarColumn(IsIgnore = true)] public override bool PhoneNumberConfirmed { get; set; }
    [SugarColumn(ColumnName = "display_name")] public string DisplayName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "is_enabled")] public bool IsEnabled { get; set; } = true;
    [SugarColumn(ColumnName = "created_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [SugarColumn(ColumnName = "updated_at", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [SugarColumn(ColumnName = "authenticator_key", IsNullable = true)] public string? ProtectedAuthenticatorKey { get; set; }
    [SugarColumn(ColumnName = "recovery_codes", IsNullable = true)] public string? RecoveryCodes { get; set; }
}
