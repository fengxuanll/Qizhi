using Qizhu.Application.Common;
using Qizhu.Domain.Projects;

namespace Qizhu.Application.Projects;

/// <summary>所有读取显式携带租户，避免后续数据库实现遗漏隔离条件。</summary>
public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);
    Task<Project?> FindAsync(long tenantId, long projectId, CancellationToken cancellationToken);
    Task<PagedResult<Project>> ListAsync(long tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
