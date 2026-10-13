using System.Collections;
using System.Text;
using System.Text.Json;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Maps consolidated <see cref="ProcessorOptions"/> onto the option dictionaries used by the
/// chunk-based processors in <c>MCPsharp.Services.Streaming</c> and by the consolidated processors.
/// </summary>
internal static class StreamProcessorOptionMapper
{
    /// <summary>
    /// Builds a case-insensitive option dictionary from <see cref="ProcessorOptions.Parameters"/>,
    /// converting JSON values (as produced by MCP argument deserialization) to plain CLR values.
    /// </summary>
    public static Dictionary<string, object> ToOptionDictionary(ProcessorOptions? options)
        => ToOptionDictionary(options?.Parameters);

    /// <summary>
    /// Builds a case-insensitive option dictionary from raw processor parameters, converting JSON values
    /// (as produced by MCP argument deserialization) to plain CLR values.
    /// </summary>
    public static Dictionary<string, object> ToOptionDictionary(IEnumerable<KeyValuePair<string, object>>? parameters)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (parameters == null)
            return result;

        foreach (var (key, value) in parameters)
        {
            var normalized = Normalize(value);
            if (normalized != null)
                result[key] = normalized;
        }

        return result;
    }

    /// <summary>
    /// Resolves the text encoding named by <see cref="ProcessorOptions.Encoding"/> (UTF-8 by default).
    /// UTF-8 is returned without a byte order mark so writers do not prepend one to the output.
    /// </summary>
    public static Encoding ResolveEncoding(ProcessorOptions? options)
    {
        var name = options?.Encoding;
        var encoding = string.IsNullOrWhiteSpace(name) ? Encoding.UTF8 : Encoding.GetEncoding(name);
        return encoding is UTF8Encoding ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : encoding;
    }

    public static string? GetString(Dictionary<string, object> options, string key)
        => options.TryGetValue(key, out var value) ? value as string : null;

    public static bool GetBool(Dictionary<string, object> options, string key, bool defaultValue)
        => options.TryGetValue(key, out var value) && value is bool flag ? flag : defaultValue;

    public static List<string> GetStringList(Dictionary<string, object> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
            return new List<string>();

        return value switch
        {
            string single => new List<string> { single },
            List<object> list => list.OfType<string>().ToList(),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// Replaces a string-valued option with its UTF-8 bytes so byte-oriented processors can consume it
    /// </summary>
    public static void ConvertStringToBytes(Dictionary<string, object> options, string key)
    {
        if (options.TryGetValue(key, out var value) && value is string text)
            options[key] = Encoding.UTF8.GetBytes(text);
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        JsonElement element => FromJson(element),
        string or byte[] => value,
        IDictionary dictionary => NormalizeDictionary(dictionary),
        IList list => list.Cast<object?>().Select(Normalize).OfType<object>().ToList(),
        _ => value
    };

    private static Dictionary<string, object> NormalizeDictionary(IDictionary dictionary)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            var normalized = Normalize(entry.Value);
            if (normalized != null && entry.Key.ToString() is { } key)
                result[key] = normalized;
        }

        return result;
    }

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(FromJson).OfType<object>().ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .Select(p => (p.Name, Value: FromJson(p.Value)))
            .Where(p => p.Value != null)
            .ToDictionary(p => p.Name, p => p.Value!, StringComparer.OrdinalIgnoreCase),
        _ => null
    };
}
