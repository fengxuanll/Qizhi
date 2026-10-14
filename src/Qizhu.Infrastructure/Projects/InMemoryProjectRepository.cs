using System.Collections.Concurrent;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;
using Qizhu.Domain.Projects;

namespace Qizhu.Infrastructure.Projects;

/// <summary>仅用于开发示例：进程重启后数据清空，不支持跨实例共享。</summary>
public sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly ConcurrentDictionary<(long TenantId, long ProjectId), Project> _projects = new();

    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_projects.TryAdd((project.TenantId, project.Id), project))
        {
            throw new InvalidOperationException("项目标识重复。");
        }

        return Task.CompletedTask;
    }

    public Task<Project?> FindAsync(long tenantId, long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _projects.TryGetValue((tenantId, projectId), out var project);
        return Task.FromResult(project);
    }

    public Task<PagedResult<Project>> ListAsync(long tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _projects.Values
            .Where(project => project.TenantId == tenantId)
            .OrderByDescending(project => project.CreatedAt)
            .ThenBy(project => project.Id)
            .ToArray();

        // 使用 long 避免极大页码乘法溢出；超出范围返回空页。
        var offset = ((long)pageNumber - 1) * pageSize;
        var items = offset >= snapshot.Length ? [] : snapshot.Skip((int)offset).Take(pageSize).ToArray();
        return Task.FromResult(new PagedResult<Project>(items, snapshot.Length, pageNumber, pageSize));
    }
}
