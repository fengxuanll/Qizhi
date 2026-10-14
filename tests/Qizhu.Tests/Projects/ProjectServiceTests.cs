using Qizhu.Application.Abstractions;
using Qizhu.Application.Projects;
using Qizhu.Domain.Common;
using Qizhu.Infrastructure.Projects;

namespace Qizhu.Tests.Projects;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task TenantCannotReadAnotherTenantsProject()
    {
        var repository = new InMemoryProjectRepository();
        var first = new ProjectService(repository, new TestTenant(TestIds.Next()), TimeProvider.System, TestIds.Generator);
        var second = new ProjectService(repository, new TestTenant(TestIds.Next()), TimeProvider.System, TestIds.Generator);
        var created = await first.CreateAsync("租户 A 的项目");

        Assert.Null(await second.FindAsync(created.Id));
        Assert.Empty((await second.ListAsync(1, 20)).Items);
        Assert.Equal(1, (await first.ListAsync(1, 20)).TotalCount);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPaginationReturnsBusinessError(int pageNumber, int pageSize)
    {
        var service = new ProjectService(new InMemoryProjectRepository(), new TestTenant(TestIds.Next()), TimeProvider.System, TestIds.Generator);
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ListAsync(pageNumber, pageSize));
        Assert.Equal("pagination.invalid", exception.Code);
    }

    [Fact]
    public async Task VeryLargePageDoesNotOverflowOrReturnFirstPage()
    {
        var service = new ProjectService(new InMemoryProjectRepository(), new TestTenant(TestIds.Next()), TimeProvider.System, TestIds.Generator);
        await service.CreateAsync("示例");
        var page = await service.ListAsync(int.MaxValue, 100);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task CancellationDoesNotInsertProject()
    {
        var service = new ProjectService(new InMemoryProjectRepository(), new TestTenant(TestIds.Next()), TimeProvider.System, TestIds.Generator);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync("示例", cancellation.Token));
        Assert.Empty((await service.ListAsync(1, 20)).Items);
    }

    private sealed record TestTenant(long Id) : ICurrentTenant;
}
