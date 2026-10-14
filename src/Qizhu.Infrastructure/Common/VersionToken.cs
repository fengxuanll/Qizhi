using Qizhu.Application.Common;

namespace Qizhu.Infrastructure.Common;

public static class VersionToken
{
    public static string Encode(byte[] version) => Convert.ToBase64String(version).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string? etag)
    {
        if (string.IsNullOrEmpty(etag))
        {
            throw new ApiException(428, "concurrency.precondition_required", "请提供记录版本后再修改。");
        }

        if (etag.Length != 13 || etag[0] != '"' || etag[^1] != '"')
        {
            throw new ApiException(400, "concurrency.invalid_version", "记录版本格式不正确。");
        }

        try
        {
            var bytes = Convert.FromBase64String(etag[1..^1].Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length == 8) { return bytes; }
        }
        catch (FormatException) { }
        throw new ApiException(400, "concurrency.invalid_version", "记录版本格式不正确。");
    }
}
