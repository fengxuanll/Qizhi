namespace Qizhu.Domain.Common;

/// <summary>可向用户解释的业务规则错误，不能用于包装未知系统异常。</summary>
public sealed class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
