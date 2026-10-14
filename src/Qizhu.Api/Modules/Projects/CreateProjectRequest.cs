using System.ComponentModel.DataAnnotations;
using Qizhu.Domain.Projects;

namespace Qizhu.Api.Modules.Projects;

public sealed class CreateProjectRequest
{
    [Required(ErrorMessage = "请输入项目名称。")]
    [StringLength(Project.MaximumNameLength, ErrorMessage = "项目名称不能超过 100 个字符。")]
    public string Name { get; init; } = string.Empty;
}
