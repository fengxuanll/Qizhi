using Qizhu.Infrastructure.Common;

namespace Qizhu.Tests;

internal static class TestIds
{
    internal static readonly SnowflakeIdGenerator Generator = new(TimeProvider.System, new InMemorySnowflakeStateStore(), 1);
    internal static long Next() => Generator.NextId();
}
