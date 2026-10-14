namespace Qizhu.Application.Abstractions;

/// <summary>当前请求的租户。实现必须保证身份来源可信，不能直接信任客户端请求头。</summary>
public interface ICurrentTenant
{
    long Id { get; }
}
