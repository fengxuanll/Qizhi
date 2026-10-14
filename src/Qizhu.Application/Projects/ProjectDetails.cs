namespace Qizhu.Application.Projects;

public sealed record ProjectDetails(long Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string Version, bool IsDeleted);
