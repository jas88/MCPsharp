using System.Text.RegularExpressions;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Log processor: filters log entries by severity and/or pattern.
/// Parameters: <c>MinLevel</c> (trace, debug, info, warn, error, fatal), <c>Levels</c> (explicit list
/// of levels to keep), <c>Pattern</c> (regex the entry's first line must match) and <c>IgnoreCase</c>.
/// The level is the first level token on a line (e.g. INFO, Warning, [ERR]); lines without one are
/// continuation lines (such as stack traces) and follow the decision for the entry they belong to.
/// Kept lines are written in the input's encoding, with a byte order mark only when the input had one.
/// </summary>
public partial class LogStreamProcessor : IStreamProcessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly string[] LevelNames = { "trace", "debug", "info", "warn", "error", "fatal" };

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("log", () => FilterAsync(input, output, context, ct));

    private static async Task<ProcessingResult> FilterAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
    {
        var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
        var allowedLevels = ResolveAllowedLevels(options);
        var regexOptions = StreamProcessorOptionMapper.GetBool(options, "IgnoreCase", false) ? RegexOptions.IgnoreCase : RegexOptions.None;
        var pattern = StreamProcessorOptionMapper.GetString(options, "Pattern") is { Length: > 0 } patternText
            ? new Regex(patternText, regexOptions, RegexTimeout)
            : null;

        var text = await TextStreamEncoding.DetectAsync(input, StreamProcessorOptionMapper.ResolveEncoding(context.Options), ct).ConfigureAwait(false);
        await text.WriteByteOrderMarkAsync(output, ct).ConfigureAwait(false);
        var levelCounts = LevelNames.ToDictionary(name => name, _ => 0L);
        long linesRead = 0;
        long linesWritten = 0;
        long entriesKept = 0;
        var includeCurrentEntry = false;
        var seenEntry = false;

        using var reader = text.CreateReader();
        await using var writer = new StreamWriter(output, text.Encoding, bufferSize: 4096, leaveOpen: true) { NewLine = "\n" };
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            linesRead++;
            var level = DetectLevel(line);
            if (level != null)
            {
                seenEntry = true;
                levelCounts[level]++;
                includeCurrentEntry = (allowedLevels == null || allowedLevels.Contains(level))
                    && (pattern == null || pattern.IsMatch(line));
                if (includeCurrentEntry)
                    entriesKept++;
            }
            else if (!seenEntry)
            {
                // Lines before the first recognised entry belong to no entry: keep them only without a level filter
                includeCurrentEntry = allowedLevels == null && (pattern == null || pattern.IsMatch(line));
            }

            if (!includeCurrentEntry)
                continue;

            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            linesWritten++;
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);

        var metrics = new Dictionary<string, object>
        {
            ["LinesRead"] = linesRead,
            ["LinesWritten"] = linesWritten,
            ["EntriesKept"] = entriesKept
        };
        foreach (var (name, count) in levelCounts)
            metrics[$"Level.{name}"] = count;

        return new ProcessingResult
        {
            Success = true,
            RecordsProcessed = linesRead,
            BytesProcessed = input.CanSeek ? input.Length : null,
            Message = $"Kept {linesWritten} of {linesRead} log line(s) ({entriesKept} entr{(entriesKept == 1 ? "y" : "ies")})",
            Metrics = metrics
        };
    }

    private static HashSet<string>? ResolveAllowedLevels(Dictionary<string, object> options)
    {
        var explicitLevels = StreamProcessorOptionMapper.GetStringList(options, "Levels");
        if (explicitLevels.Count > 0)
            return explicitLevels.Select(NormalizeLevel).ToHashSet(StringComparer.Ordinal);

        if (StreamProcessorOptionMapper.GetString(options, "MinLevel") is not { } minLevel)
            return null;

        var minIndex = Array.IndexOf(LevelNames, NormalizeLevel(minLevel));
        return LevelNames.Skip(minIndex).ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeLevel(string name) => name.Trim().ToUpperInvariant() switch
    {
        "TRACE" or "VERBOSE" or "TRC" or "VRB" => "trace",
        "DEBUG" or "DBG" => "debug",
        "INFO" or "INFORMATION" or "INF" => "info",
        "WARN" or "WARNING" or "WRN" => "warn",
        "ERROR" or "ERR" => "error",
        "FATAL" or "CRITICAL" or "FTL" or "CRT" or "CRIT" => "fatal",
        _ => throw new ArgumentException($"Unknown log level '{name}'. Expected one of: {string.Join(", ", LevelNames)}")
    };

    private static string? DetectLevel(string line)
    {
        var match = LevelTokenRegex().Match(line);
        return match.Success ? NormalizeLevel(match.Value) : null;
    }

    // Full level names in any case; three-letter abbreviations (Serilog style) only in upper case
    [GeneratedRegex(@"(?i:\b(?:TRACE|VERBOSE|DEBUG|INFO|INFORMATION|WARN|WARNING|ERROR|FATAL|CRITICAL)\b)|\b(?:TRC|VRB|DBG|INF|WRN|ERR|FTL|CRT)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LevelTokenRegex();

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "log", "txt", "out", "err" },
            SupportedOutputFormats = new List<string> { "log", "txt" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { "MinLevel", "Levels", "Pattern", "IgnoreCase" }
        });
}
