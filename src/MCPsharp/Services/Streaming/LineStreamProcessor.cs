using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Models.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Line-based stream processor for text files. Empty lines are kept unless the RemoveEmptyLines option
/// is true (TrimWhitespace, on by default, makes whitespace-only lines empty). With IncludeLineNumbers,
/// lines are numbered from the chunk's <see cref="StartLineNumberKey"/> metadata value.
/// </summary>
public partial class LineStreamProcessor : IStreamProcessor
{
    /// <summary>
    /// Chunk metadata key holding the line number of the chunk's first line (long or int, default 1)
    /// </summary>
    public const string StartLineNumberKey = "StartLineNumber";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private readonly ILogger<LineStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.LineProcessor;

    public LineStreamProcessor(ILogger<LineStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<LineStreamProcessor>.Instance;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid line processor options")]
    private static partial void LogInvalidOptions(ILogger logger, Exception exception);

    public Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options)
    {
        try
        {
            return Task.FromResult(ProcessChunk(chunk, options));
        }
        catch (Exception ex)
        {
            return Task.FromException<StreamChunk>(ex);
        }
    }

    private static StreamChunk ProcessChunk(StreamChunk chunk, Dictionary<string, object> options)
    {
        var config = ParseConfig(options);
        var encoding = config.Encoding ?? Encoding.UTF8;
        var lines = TextChunkLines.Split(encoding.GetString(chunk.Data), out var endsWithLineBreak);
        var firstLineNumber = GetStartLineNumber(chunk);
        var processedLines = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            // Null means the line was filtered out; an empty string is an empty line and is kept
            var processedLine = ProcessLine(lines[i], config, firstLineNumber + i);
            if (processedLine != null)
            {
                processedLines.Add(processedLine);
            }
        }

        var processedText = TextChunkLines.Join(processedLines, endsWithLineBreak);
        var processedData = encoding.GetBytes(processedText);

        return new StreamChunk
        {
            Data = processedData,
            Position = chunk.Position,
            Length = processedData.Length,
            ChunkIndex = chunk.ChunkIndex,
            IsLastChunk = chunk.IsLastChunk,
            Metadata = chunk.Metadata,
            Encoding = config.Encoding?.BodyName,
            Lines = processedLines,
            Checksum = ComputeChecksum(processedData)
        };
    }

    /// <summary>
    /// Line number of the chunk's first line, from <see cref="StartLineNumberKey"/> in the chunk metadata
    /// (1 when absent). Callers that split one input into several chunks set it so numbering continues
    /// across chunks; keeping it on the chunk rather than in the processor lets one processor instance
    /// serve concurrent runs.
    /// </summary>
    private static long GetStartLineNumber(StreamChunk chunk)
        => chunk.Metadata.TryGetValue(StartLineNumberKey, out var value)
            ? value switch { long number => number, int number => number, _ => 1 }
            : 1;

    public Task<bool> ValidateOptionsAsync(Dictionary<string, object> options)
    {
        try
        {
            ParseConfig(options);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogInvalidOptions(_logger, ex);
            return Task.FromResult(false);
        }
    }

    private static LineProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new LineProcessorConfig();

        if (options.TryGetValue("LineFilter", out var filter) && filter is string filterStr)
            config.LineFilter = filterStr;

        if (options.TryGetValue("LineReplacement", out var replacement) && replacement is string replacementStr)
            config.LineReplacement = replacementStr;

        if (options.TryGetValue("IncludeLineNumbers", out var includeNumbers) && includeNumbers is bool include)
            config.IncludeLineNumbers = include;

        if (options.TryGetValue("LinePrefix", out var prefix) && prefix is string prefixStr)
            config.LinePrefix = prefixStr;

        if (options.TryGetValue("LineSuffix", out var suffix) && suffix is string suffixStr)
            config.LineSuffix = suffixStr;

        if (options.TryGetValue("RemoveEmptyLines", out var removeEmpty) && removeEmpty is bool remove)
            config.RemoveEmptyLines = remove;

        if (options.TryGetValue("FieldSeparator", out var separator) && separator is string sepStr)
            config.FieldSeparator = sepStr;

        if (options.TryGetValue("SelectedFields", out var fields) && fields is List<object> fieldList)
            config.SelectedFields = fieldList.Cast<int>().ToList();

        if (options.TryGetValue("TrimWhitespace", out var trim) && trim is bool trimWhitespace)
            config.TrimWhitespace = trimWhitespace;

        if (options.TryGetValue("Encoding", out var encoding) && encoding is string encodingStr)
            config.Encoding = Encoding.GetEncoding(encodingStr);

        return config;
    }

    private static string? ProcessLine(string line, LineProcessorConfig config, long lineNumber)
    {
        // Trim whitespace if configured
        if (config.TrimWhitespace)
        {
            line = line.Trim();
        }

        // Skip empty lines if configured
        if (config.RemoveEmptyLines && string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // Apply line filter
        if (!string.IsNullOrEmpty(config.LineFilter) && !Regex.IsMatch(line, config.LineFilter, RegexOptions.None, RegexTimeout))
        {
            return null;
        }

        // Apply line replacement
        if (!string.IsNullOrEmpty(config.LineReplacement))
        {
            line = Regex.Replace(line, config.LineFilter ?? ".*", config.LineReplacement, RegexOptions.None, RegexTimeout);
        }

        // Field processing
        if (!string.IsNullOrEmpty(config.FieldSeparator))
        {
            var fields = line.Split(new[] { config.FieldSeparator }, StringSplitOptions.None);

            if (config.SelectedFields.Count > 0)
            {
                var selectedFields = config.SelectedFields
                    .Where(i => i >= 0 && i < fields.Length)
                    .Select(i => fields[i]);
                line = string.Join(config.FieldSeparator, selectedFields);
            }
        }

        // Add prefix and suffix
        if (!string.IsNullOrEmpty(config.LinePrefix))
        {
            line = config.LinePrefix + line;
        }

        if (!string.IsNullOrEmpty(config.LineSuffix))
        {
            line = line + config.LineSuffix;
        }

        // Add line numbers
        if (config.IncludeLineNumbers)
        {
            // Line number is passed as parameter
            line = $"{lineNumber}: {line}";
        }

        return line;
    }

    private static string ComputeChecksum(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
