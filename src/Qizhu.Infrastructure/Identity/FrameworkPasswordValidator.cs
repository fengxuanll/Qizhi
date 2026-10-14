using Microsoft.AspNetCore.Identity;

namespace Qizhu.Infrastructure.Identity;

public sealed class FrameworkPasswordValidator : IPasswordValidator<FrameworkUser>
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
        { "password123456789", "123456789012345", "qwertyuiopasdfgh", "administrator123", "12345678901234567890" };
    public Task<IdentityResult> ValidateAsync(UserManager<FrameworkUser> manager, FrameworkUser user, string? password)
    {
        var valid = password is { Length: >= 15 and <= 128 } && password.Distinct().Count() > 2 && !CommonPasswords.Contains(password);
        return Task.FromResult(valid ? IdentityResult.Success : IdentityResult.Failed(new IdentityError
        { Code = "PasswordPolicy", Description = "密码需为 15 到 128 个字符，且不能使用常见弱密码。" }));
    }
}
