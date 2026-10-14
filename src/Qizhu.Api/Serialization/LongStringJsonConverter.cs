using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qizhu.Api.Serialization;

/// <summary>long 的传输契约统一为十进制字符串；兼容读取准确的整数 JSON 输入。</summary>
public sealed class LongStringJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number)) { return number; }
        if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) { return value; }
        throw new JsonException("必须提供有效的 long 十进制字符串。");
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
