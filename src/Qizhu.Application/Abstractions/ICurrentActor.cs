namespace Qizhu.Application.Abstractions;

/// <summary>从已验证身份取得全局用户与当前会话的雪花 ID；审计追踪标识来自当前请求。</summary>
public interface ICurrentActor
{
    long UserId { get; }
    long SessionId { get; }
    string RequestId { get; }
    string TraceId { get; }
}
