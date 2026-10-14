using Qizhu.Domain.Projects;

namespace Qizhu.Application.Projects;

public sealed record ProjectDto(long Id, string Name, DateTimeOffset CreatedAt)
{
    public static ProjectDto From(Project project) => new(project.Id, project.Name, project.CreatedAt);
}
