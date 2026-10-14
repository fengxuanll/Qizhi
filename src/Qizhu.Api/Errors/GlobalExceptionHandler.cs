using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Qizhu.Application.Common;
using Qizhu.Domain.Common;

namespace Qizhu.Api.Errors;

public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var businessError = exception as BusinessRuleException;
        var apiError = exception as ApiException;
        var badRequest = exception as BadHttpRequestException;
        var status = apiError?.StatusCode ?? (businessError is not null ? StatusCodes.Status400BadRequest
            : badRequest?.StatusCode ?? StatusCodes.Status500InternalServerError);

        if (status >= 500)
        {
            logger.LogError("请求处理失败：{ExceptionType}，追踪编号：{TraceId}", exception.GetType().Name, context.TraceIdentifier);
        }

        context.Response.StatusCode = status;
        if (apiError?.Code == "idempotency.in_progress") { context.Response.Headers.RetryAfter = "2"; }
        if (status == 429) { context.Response.Headers.RetryAfter = (60 - DateTimeOffset.UtcNow.Second).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = apiError is not null ? "操作未完成" : businessError is not null ? "业务校验未通过" : status < 500 ? "请求格式不正确" : "服务暂时无法处理请求",
                Detail = apiError?.Message ?? businessError?.Message ?? (status < 500 ? "请检查请求参数和 JSON 格式。" : "请稍后重试，或向管理员提供追踪编号。"),
                Extensions = { ["code"] = apiError?.Code ?? businessError?.Code ?? (status < 500 ? "request.invalid" : "server.error") }
            }
        });
    }
}
