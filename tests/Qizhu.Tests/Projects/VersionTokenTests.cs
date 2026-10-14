using Qizhu.Application.Common;
using Qizhu.Infrastructure.Common;

namespace Qizhu.Tests.Projects;

public sealed class VersionTokenTests
{
    [Fact]
    public void EncodesAndDecodesAnEightByteVersion()
    {
        byte[] version = [0, 1, 2, 3, 250, 251, 252, 255];
        var value = VersionToken.Encode(version);
        Assert.Equal(version, VersionToken.Decode('"' + value + '"'));
    }
    [Theory]
    [InlineData(null, 428)]
    [InlineData("*", 400)]
    [InlineData("W/\"AQIDBAUGBwg\"", 400)]
    [InlineData("invalid", 400)]
    public void RejectsMissingOrWeakVersions(string? version, int expectedStatus)
    {
        Assert.Equal(expectedStatus, Assert.Throws<ApiException>(() => VersionToken.Decode(version)).StatusCode);
    }
}
