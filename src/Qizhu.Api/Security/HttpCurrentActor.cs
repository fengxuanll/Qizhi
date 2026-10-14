using System.Diagnostics;
using System.Security.Claims;
using Qizhu.Application.Abstractions;
using Qizhu.Application.Common;

namespace Qizhu.Api.Security;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public long UserId => ReadId(ClaimTypes.NameIdentifier);
    public long SessionId => ReadId("session_id");
    public string RequestId => accessor.HttpContext?.TraceIdentifier ?? string.Empty;
    public string TraceId => Activity.Current?.TraceId.ToString() ?? string.Empty;
    private long ReadId(string claim) => long.TryParse(accessor.HttpContext?.User.FindFirstValue(claim), out var value) && value > 0
        ? value : throw new ApiException(401, "auth.unauthenticated", "请先登录。");
}
