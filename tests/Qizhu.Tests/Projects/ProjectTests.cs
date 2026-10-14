using Qizhu.Domain.Common;
using Qizhu.Domain.Projects;

namespace Qizhu.Tests.Projects;

public sealed class ProjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankName(string? name)
    {
        var exception = Assert.Throws<BusinessRuleException>(() => Project.Create(TestIds.Next(), TestIds.Next(), name, DateTimeOffset.UtcNow));
        Assert.Equal("project.name_required", exception.Code);
    }

    [Fact]
    public void Create_TrimsNameAndKeepsProvidedTimestamp()
    {
        var now = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        var project = Project.Create(TestIds.Next(), TestIds.Next(), "  客户管理  ", now);
        Assert.Equal("客户管理", project.Name);
        Assert.Equal(now, project.CreatedAt);
        Assert.NotEqual(0L, project.Id);
    }

    [Fact]
    public void Create_RejectsOverlongName()
    {
        var exception = Assert.Throws<BusinessRuleException>(() => Project.Create(TestIds.Next(), TestIds.Next(), new string('a', 101), DateTimeOffset.UtcNow));
        Assert.Equal("project.name_too_long", exception.Code);
    }

    [Fact]
    public void Create_RejectsEmptyTenant()
    {
        Assert.Throws<BusinessRuleException>(() => Project.Create(TestIds.Next(), 0L, "示例", DateTimeOffset.UtcNow));
    }
}
