using Microsoft.AspNetCore.Mvc;
using Qizhu.Api.Security;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;

namespace Qizhu.Api.Modules.Projects;

[ApiController]
[Route("api/v1/projects")]
public sealed class WorkspaceProjectsController(IProjectWorkspace workspace) : ControllerBase
{
    [HttpGet]
    [RequireTenantPermission("projects.projects.read")]
    public Task<PagedResult<ProjectDetails>> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null, [FromQuery] bool deleted = false, [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDirection = "desc", [FromQuery] DateTimeOffset? createdFrom = null, [FromQuery] DateTimeOffset? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(["pageNumber", "pageSize", "keyword", "deleted", "sortBy", "sortDirection", "createdFrom", "createdTo"], StringComparer.OrdinalIgnoreCase);
        if (Request.Query.Keys.Any(key => !allowed.Contains(key))) { throw new ApiException(400, "request.invalid", "包含不支持的查询参数。"); }
        return workspace.ListAsync(new ProjectQuery(pageNumber, pageSize, keyword, deleted, sortBy, sortDirection, createdFrom, createdTo), cancellationToken);
    }

    [HttpGet("{id:long}")]
    [RequireTenantPermission("projects.projects.read")]
    public async Task<ActionResult<ProjectDetails>> Find(long id, CancellationToken cancellationToken)
    {
        var item = await workspace.FindAsync(id, cancellationToken);
        if (item is null) { throw new ApiException(404, "resource.not_found", "项目不存在。"); }
        Response.Headers.ETag = '"' + item.Version + '"';
        return Ok(item);
    }
    [HttpPost]
    [RequireTenantPermission("projects.projects.create")]
    public async Task<IActionResult> Create(CreateProjectRequest request, CancellationToken cancellationToken) =>
        Respond(await workspace.CreateAsync(request.Name, Context(), cancellationToken));
    [HttpPut("{id:long}")]
    [RequireTenantPermission("projects.projects.update")]
    public async Task<IActionResult> Update(long id, CreateProjectRequest request, CancellationToken cancellationToken) =>
        Respond(await workspace.UpdateAsync(id, request.Name, Context(), cancellationToken));
    [HttpDelete("{id:long}")]
    [RequireTenantPermission("projects.projects.delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        Respond(await workspace.DeleteAsync(id, Context(), cancellationToken));
    [HttpPost("{id:long}/restore")]
    [RequireTenantPermission("projects.projects.restore")]
    public async Task<IActionResult> Restore(long id, CancellationToken cancellationToken) =>
        Respond(await workspace.RestoreAsync(id, Context(), cancellationToken));

    private MutationContext Context() => new(Request.Headers["Idempotency-Key"].ToString(), Request.Headers.IfMatch.ToString());
    private IActionResult Respond(MutationResult result)
    {
        if (result.Location is not null) { Response.Headers.Location = result.Location; }
        if (result.Version is not null) { Response.Headers.ETag = '"' + result.Version + '"'; }
        return result.StatusCode == 204 ? NoContent() : StatusCode(result.StatusCode, result.Value);
    }
}
