using Qizhu.Application.Abstractions;

namespace Qizhu.Infrastructure.Common;

/// <summary>持久化节点时间水位，防止同一节点进程重启或并行启动后复用时间片。</summary>
public interface ISnowflakeStateStore
{
    /// <summary>原子预留一个大于已有水位的毫秒时间片，返回相对纪元的毫秒数。</summary>
    long ReserveTimestamp(int workerId, long currentMilliseconds);
}

/// <summary>雪花主键：41 位毫秒时间 + 10 位节点 + 12 位序列；2025-01-01 UTC 为纪元。</summary>
/// <remarks>每个时间片可分配 4096 个 ID。数据库水位通过 CAS 更新，同节点的多个进程也不会重复。
/// 时钟回退直接拒绝生成；水位最多领先真实时间 5 秒，避免持续错时或无限超额分配。</remarks>
public sealed class SnowflakeIdGenerator(TimeProvider clock, ISnowflakeStateStore state, int workerId) : IIdGenerator
{
    public static readonly DateTimeOffset Epoch = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly object _gate = new();
    private readonly int _workerId = workerId is >= 0 and <= 1022 ? workerId : throw new ArgumentOutOfRangeException(nameof(workerId), "节点号必须在 0 到 1022；1023 保留给历史迁移。");
    private long _timestamp = -1;
    private long _observed = -1;
    private int _sequence = 4096;

    public long NextId()
    {
        lock (_gate)
        {
            var now = (clock.GetUtcNow() - Epoch).Ticks / TimeSpan.TicksPerMillisecond;
            if (now < 0 || now < _observed) { throw new InvalidOperationException("雪花 ID 时钟回退，请校准 UTC 时钟后重试。"); }
            _observed = now;
            if (_sequence == 4096 || now > _timestamp)
            {
                _timestamp = state.ReserveTimestamp(_workerId, now);
                _sequence = 0;
            }
            if (_timestamp >= (1L << 41) || _timestamp < now || _timestamp > now + 5000)
            {
                _sequence = 4096;
                throw new InvalidOperationException("雪花 ID 时间水位超出允许范围。");
            }
            // 纪元的第一个毫秒、节点 0、序列 0 会组合成零，保留零作为无效 ID。
            if (_timestamp == 0 && _workerId == 0 && _sequence == 0) { _sequence = 1; }
            return (_timestamp << 22) | ((long)_workerId << 12) | (uint)_sequence++;
        }
    }
}

/// <summary>仅供 Memory 演示和测试使用；正式部署由 SQL Server 保存水位。</summary>
public sealed class InMemorySnowflakeStateStore : ISnowflakeStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, long> _watermarks = [];
    public long ReserveTimestamp(int workerId, long currentMilliseconds)
    {
        lock (_gate)
        {
            var previous = _watermarks.GetValueOrDefault(workerId, -1);
            return _watermarks[workerId] = Math.Max(currentMilliseconds, previous + 1);
        }
    }
}
