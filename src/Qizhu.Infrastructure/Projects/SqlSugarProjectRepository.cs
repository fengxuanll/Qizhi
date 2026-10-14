using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;
using Qizhu.Domain.Projects;
using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Projects;

public sealed class SqlSugarProjectRepository : IProjectRepository
{
    private readonly SqlSugarClient _database;
    public SqlSugarProjectRepository(IDatabaseConnection connection, ICurrentTenant tenant)
    {
        _database = connection.Client;
        var tenantId = tenant.Id;
        _database.QueryFilter.AddTableFilter<ProjectRecord>(item => item.TenantId == tenantId && !item.IsDeleted);
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.Insertable(new ProjectRecord
        {
            Id = project.Id,
            TenantId = project.TenantId,
            Name = project.Name,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.CreatedAt
        }).ExecuteCommandAsync();
    }
    public async Task<Project?> FindAsync(long tenantId, long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = await _database.Queryable<ProjectRecord>().Where(item => item.TenantId == tenantId && item.Id == projectId && !item.IsDeleted).SingleAsync();
        return record is null ? null : Project.Restore(record.Id, record.TenantId, record.Name, record.CreatedAt);
    }
    public async Task<PagedResult<Project>> ListAsync(long tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = new RefAsync<int>();
        var records = await _database.Queryable<ProjectRecord>().Where(item => item.TenantId == tenantId && !item.IsDeleted)
            .OrderBy(item => item.CreatedAt, OrderByType.Desc).OrderBy(item => item.Id)
            .ToPageListAsync(pageNumber, pageSize, count);
        return new PagedResult<Project>(records.Select(item => Project.Restore(item.Id, item.TenantId, item.Name, item.CreatedAt)).ToArray(), count.Value, pageNumber, pageSize);
    }
}
