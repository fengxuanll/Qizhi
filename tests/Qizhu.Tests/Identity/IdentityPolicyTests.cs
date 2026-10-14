using Qizhu.Infrastructure.Identity;

namespace Qizhu.Tests.Identity;

public sealed class IdentityPolicyTests
{
    [Fact]
    public void NormalizationHandlesCompatibilityCharactersAndWhitespace()
    {
        Assert.Equal("ADMIN", new FrameworkLookupNormalizer().NormalizeName(" Ａｄｍｉｎ "));
    }
}
