using Microsoft.AspNetCore.Antiforgery;
using Qizhu.Application.Common;

namespace Qizhu.Api.Security;

public sealed class AntiforgeryValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method)
            || context.Request.Path.StartsWithSegments("/api") && (HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method)))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { throw new ApiException(403, "request.csrf_invalid", "页面安全凭证已过期，请刷新后重试。"); }
        }
        await next(context);
    }
}
