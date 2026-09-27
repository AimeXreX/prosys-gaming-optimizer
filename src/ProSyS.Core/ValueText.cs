using System.Globalization;
using System.Text.Json;

namespace ProSyS.Core;

/// <summary>
/// Produces one comparable text form for a captured value, whether it is a live value
/// (int, long, string, string[], byte[]) or the same value read back from a JSON backup.
/// </summary>
public static class ValueText
{
    public static string? Canonical(object? value, string? kind = null) => value switch
    {
        null => null,
        JsonElement element => FromJson(element, kind),
        byte[] bytes => Convert.ToHexString(bytes),
        string[] lines => string.Join('\n', lines),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static string? FromJson(JsonElement element, string? kind) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String when kind == "Binary" && element.TryGetBytesFromBase64(out var bytes) => Convert.ToHexString(bytes),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Array => string.Join('\n', element.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())),
        _ => element.GetRawText()
    };
}
