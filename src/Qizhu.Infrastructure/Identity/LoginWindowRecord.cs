using Qizhu.Infrastructure.Database;
using SqlSugar;

namespace Qizhu.Infrastructure.Identity;

/// <summary>账号和 IP 的数据库登录限额；窗口开始时间必须为 UTC 整分钟。</summary>
[SugarTable("dbo.identity_login_windows")]
public sealed class LoginWindowRecord : EntityRecord
{
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "key_hash")] public byte[] KeyHash { get; set; } = [];
    [SugarColumn(ColumnName = "window_start", ColumnDataType = "datetimeoffset(7)")] public DateTimeOffset WindowStart { get; set; }
    [SugarColumn(ColumnName = "request_count")] public int RequestCount { get; set; }
}
