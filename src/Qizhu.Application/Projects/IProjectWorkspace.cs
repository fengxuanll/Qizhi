using Qizhu.Application.Common;

namespace Qizhu.Application.Projects;

/// <summary>当前租户项目查询；时间为包含起点、不包含终点的范围，查询时归一化 UTC。</summary>
public sealed record ProjectQuery(int PageNumber = 1, int PageSize = 20, string? Keyword = null, bool Deleted = false,
    string SortBy = "createdAt", string SortDirection = "desc", DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null);
/// <summary>一次写操作的幂等键与强 ETag；网络结果不确定时必须保留同键、同参数、同版本重试。</summary>
public sealed record MutationContext(string Key, string? IfMatch = null);
/// <summary>与业务事务一同保存的成功结果；重放保留原状态码、资源路径及版本。</summary>
public sealed record MutationResult(int StatusCode, ProjectDetails? Value, string? Location, string? Version);

/// <summary>项目管理用例边界；实现负责租户隔离、并发校验、幂等与同事务审计。</summary>
public interface IProjectWorkspace
{
    Task<PagedResult<ProjectDetails>> ListAsync(ProjectQuery query, CancellationToken cancellationToken);
    Task<ProjectDetails?> FindAsync(long id, CancellationToken cancellationToken);
    Task<MutationResult> CreateAsync(string name, MutationContext context, CancellationToken cancellationToken);
    Task<MutationResult> UpdateAsync(long id, string name, MutationContext context, CancellationToken cancellationToken);
    /// <summary>软删除，不能物理移除记录；必须匹配当前 rowversion。</summary>
    Task<MutationResult> DeleteAsync(long id, MutationContext context, CancellationToken cancellationToken);
    /// <summary>需要独立恢复权限，清空删除审计并返回新的版本。</summary>
    Task<MutationResult> RestoreAsync(long id, MutationContext context, CancellationToken cancellationToken);
}
