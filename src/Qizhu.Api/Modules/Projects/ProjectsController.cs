using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qizhu.Application.Common;
using Qizhu.Application.Projects;

namespace Qizhu.Api.Modules.Projects;

/// <summary>演示模块，仅在 Development 且显式开启示例开关时注册。</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/projects")]
public sealed class ProjectsController(ProjectService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProjectDto>> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) => service.ListAsync(pageNumber, pageSize, cancellationToken);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ProjectDto>> Find(long id, CancellationToken cancellationToken)
    {
        var project = await service.FindAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await service.CreateAsync(request.Name, cancellationToken);
        return CreatedAtAction(nameof(Find), new { id = project.Id }, project);
    }
}
