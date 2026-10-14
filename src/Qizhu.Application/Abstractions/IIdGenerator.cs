namespace Qizhu.Application.Abstractions;

/// <summary>为业务实体分配正数 long 主键；实现必须保证多线程和进程重启后不重复。</summary>
public interface IIdGenerator
{
    /// <summary>获取雪花 ID。ID 只用于标识，业务时间以实体的 UTC 时间字段为准。</summary>
    long NextId();
}
