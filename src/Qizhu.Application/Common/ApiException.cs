namespace Qizhu.Application.Common;

/// <summary>明确的业务拒绝，不承载数据库异常或内部诊断信息。</summary>
public sealed class ApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
