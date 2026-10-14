using System.Diagnostics;

namespace Qizhu.Api.Observability;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers["X-Trace-Id"] = traceId;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["traceId"] = traceId, ["requestId"] = context.TraceIdentifier });

        try
        {
            await next(context);
        }
        finally
        {
            // 不记录请求体、查询字符串、令牌或 Cookie，避免敏感信息进入日志。
            var route = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            logger.LogInformation("{method} {routeTemplate} 返回 {statusCode}，耗时 {durationMs:F1}ms",
                context.Request.Method, route, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }
}
