using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Models.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Line-based stream processor for text files
/// </summary>
public partial class LineStreamProcessor : IStreamProcessor
{
    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
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
        var lines = ExtractLines(chunk, config.Encoding ?? Encoding.UTF8);
        var processedLines = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var processedLine = ProcessLine(lines[i], config, i + 1);
            if (!string.IsNullOrEmpty(processedLine))
            {
                processedLines.Add(processedLine);
            }
        }

        var processedText = string.Join(Environment.NewLine, processedLines);
        var processedData = (config.Encoding ?? Encoding.UTF8).GetBytes(processedText);

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

    private static List<string> ExtractLines(StreamChunk chunk, Encoding encoding)
    {
        var text = encoding.GetString(chunk.Data);
        var lines = text.Split(LineSeparators, StringSplitOptions.None).ToList();

        // Handle the case where the last line might be incomplete
        if (!chunk.IsLastChunk && lines.Count > 0 && !text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            // Keep the last line as it might be continued in the next chunk
            return lines;
        }

        return lines;
    }

    private static string? ProcessLine(string line, LineProcessorConfig config, long lineNumber = 0)
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
