using Qizhu.Infrastructure.Common;

namespace Qizhu.Tests.Identity;

public sealed class SnowflakeIdTests
{
    [Fact]
    public void RestartAndConcurrentProcessesDoNotReuseReservedTimestamp()
    {
        var clock = new TestClock(SnowflakeIdGenerator.Epoch.AddDays(1));
        var state = new InMemorySnowflakeStateStore();
        var first = new SnowflakeIdGenerator(clock, state, 7);
        var second = new SnowflakeIdGenerator(clock, state, 7);
        var values = new System.Collections.Concurrent.ConcurrentBag<long>();
        Parallel.For(0, 15000, i => values.Add((i % 2 == 0 ? first : second).NextId()));
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.All(values, id => Assert.True(id > 0));
        var restarted = new SnowflakeIdGenerator(clock, state, 7);
        Assert.DoesNotContain(restarted.NextId(), values);
    }

    [Fact]
    public void BitsContainConfiguredWorkerAndUtcTimestamp()
    {
        var clock = new TestClock(SnowflakeIdGenerator.Epoch.AddMilliseconds(123456));
        var generator = new SnowflakeIdGenerator(clock, new InMemorySnowflakeStateStore(), 123);
        var first = generator.NextId();
        Assert.Equal(123456, first >> 22);
        Assert.Equal(123, (first >> 12) & 1023);
        Assert.Equal(first + 1, generator.NextId());
    }

    [Fact]
    public void ClockRollbackFailsWithoutIssuingDuplicate()
    {
        var clock = new TestClock(SnowflakeIdGenerator.Epoch.AddDays(1));
        var generator = new SnowflakeIdGenerator(clock, new InMemorySnowflakeStateStore(), 1);
        var first = generator.NextId();
        clock.Now = clock.Now.AddMilliseconds(-1);
        Assert.Throws<InvalidOperationException>(() => generator.NextId());
        clock.Now = clock.Now.AddMilliseconds(1);
        Assert.True(generator.NextId() > first);
    }

    [Fact]
    public void MigrationWorkerCannotBeUsedByRuntime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(TimeProvider.System, new InMemorySnowflakeStateStore(), 1023));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
