using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Domain.Common;
using Qizhu.Domain.Projects;

namespace Qizhu.Application.Projects;

/// <summary>组织业务用例；不依赖 HTTP、数据库类型或宿主环境。</summary>
public sealed class ProjectService(IProjectRepository repository, ICurrentTenant currentTenant, TimeProvider clock, IIdGenerator ids)
{
    public async Task<ProjectDto> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var project = Project.Create(ids.NextId(), currentTenant.Id, name, clock.GetUtcNow());
        await repository.AddAsync(project, cancellationToken);
        return ProjectDto.From(project);
    }

    public async Task<ProjectDto?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        var project = await repository.FindAsync(currentTenant.Id, id, cancellationToken);
        return project is null ? null : ProjectDto.From(project);
    }

    public async Task<PagedResult<ProjectDto>> ListAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100)
        {
            throw new BusinessRuleException("pagination.invalid", "页码必须大于零，每页数量必须在 1 到 100 之间。");
        }

        var page = await repository.ListAsync(currentTenant.Id, pageNumber, pageSize, cancellationToken);
        return new PagedResult<ProjectDto>(page.Items.Select(ProjectDto.From).ToArray(), page.TotalCount, pageNumber, pageSize);
    }
}
