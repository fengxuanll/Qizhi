using System.Text.Json;
using Qizhu.Api.Serialization;
using Qizhu.Domain.Projects;

namespace Qizhu.Tests.Api;

public sealed class SerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new LongStringJsonConverter() } };

    [Fact]
    public void LongBeyondJavascriptSafeIntegerRoundTripsAsString()
    {
        const long id = 9007199254740993L;
        var json = JsonSerializer.Serialize(id, Options);
        Assert.Equal("\"9007199254740993\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<long>(json, Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>("\"9223372036854775808\"", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>("1.5", Options));
    }

    [Fact]
    public void DomainNormalizesNonzeroOffsetToUtc()
    {
        var source = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.FromHours(8));
        var created = Project.Create(TestIds.Next(), TestIds.Next(), "时区测试", source);
        Assert.Equal(TimeSpan.Zero, created.CreatedAt.Offset);
        Assert.Equal(source.UtcDateTime, created.CreatedAt.UtcDateTime);
    }
}
