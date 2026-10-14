using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

[SugarTable("dbo.identity_platform_roles")]
public sealed class FrameworkRole : IdentityRole<long>
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public override long Id { get; set; }
    [SugarColumn(ColumnName = "name")] public override string? Name { get; set; }
    [SugarColumn(ColumnName = "normalized_name")] public override string? NormalizedName { get; set; }
    [SugarColumn(ColumnName = "concurrency_stamp")] public override string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
