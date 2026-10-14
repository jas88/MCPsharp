using System.Security.Cryptography;
using System.Text;
using MCPsharp.Models.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// CSV stream processor
/// </summary>
public partial class CsvStreamProcessor : IStreamProcessor
{
    private readonly ILogger<CsvStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.CsvProcessor;

    public CsvStreamProcessor(ILogger<CsvStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<CsvStreamProcessor>.Instance;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid csv processor options")]
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
        var text = Encoding.UTF8.GetString(chunk.Data);
        var lines = TextChunkLines.Split(text, out var endsWithLineBreak);
        var processedLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) && config.SkipEmptyRows)
                continue;

            var fields = ParseCsvLine(line, config);

            // Apply column selection
            if (config.SelectedColumnIndexes.Count > 0)
            {
                fields = config.SelectedColumnIndexes
                    .Where(i => i >= 0 && i < fields.Length)
                    .Select(i => fields[i])
                    .ToArray();
            }

            // Apply column mappings
            if (config.ColumnMappings != null && config.ColumnMappings.Count > 0)
            {
                for (int i = 0; i < Math.Min(fields.Length, config.ColumnMappings.Count); i++)
                {
                    if (config.ColumnMappings.TryGetValue($"Column{i}", out var mappedName))
                    {
                        // This is a simplified mapping - in practice, you might want more sophisticated logic
                        fields[i] = $"{mappedName}:{fields[i]}";
                    }
                }
            }

            // Apply row filter
            if (config.RowFilter != null && !config.RowFilter(fields))
            {
                continue;
            }

            // Trim fields if configured
            if (config.TrimFields)
            {
                fields = fields.Select(f => f?.Trim() ?? "").ToArray();
            }

            processedLines.Add(string.Join(config.Delimiter, fields.Select(f => QuoteField(f, config))));
        }

        var processedText = TextChunkLines.Join(processedLines, endsWithLineBreak);
        var processedData = Encoding.UTF8.GetBytes(processedText);

        return new StreamChunk
        {
            Data = processedData,
            Position = chunk.Position,
            Length = processedData.Length,
            ChunkIndex = chunk.ChunkIndex,
            IsLastChunk = chunk.IsLastChunk,
            Metadata = chunk.Metadata,
            Encoding = "UTF-8",
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

    private static CsvProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new CsvProcessorConfig();

        if (options.TryGetValue("Delimiter", out var delimiter) && delimiter is string delimStr)
            config.Delimiter = RequireNonEmpty(delimStr, "Delimiter");

        if (options.TryGetValue("HasHeader", out var hasHeader) && hasHeader is bool hh)
            config.HasHeader = hh;

        if (options.TryGetValue("QuoteCharacter", out var quote) && quote is string quoteStr)
            config.QuoteCharacter = RequireNonEmpty(quoteStr, "QuoteCharacter");

        if (options.TryGetValue("EscapeCharacter", out var escape) && escape is string escapeStr)
            config.EscapeCharacter = escapeStr;

        if (options.TryGetValue("SelectedColumnIndexes", out var cols) && cols is List<object> colList)
            config.SelectedColumnIndexes = colList.Cast<int>().ToList();

        if (options.TryGetValue("SkipEmptyRows", out var skip) && skip is bool sr)
            config.SkipEmptyRows = sr;

        if (options.TryGetValue("TrimFields", out var trim) && trim is bool tf)
            config.TrimFields = tf;

        return config;
    }

    private static string RequireNonEmpty(string value, string optionName)
        => value.Length > 0
            ? value
            : throw new ArgumentException($"{optionName} must not be empty.", optionName);

    /// <summary>
    /// Quotes a field for output per RFC 4180 when it contains the delimiter, the quote character or a
    /// line break, doubling any embedded quote characters.
    /// </summary>
    internal static string QuoteField(string field, CsvProcessorConfig config)
    {
        var quote = config.QuoteCharacter;
        if (string.IsNullOrEmpty(quote))
            return field;

        var needsQuoting = field.Contains(config.Delimiter, StringComparison.Ordinal)
            || field.Contains(quote, StringComparison.Ordinal)
            || field.AsSpan().IndexOfAny('\r', '\n') >= 0;

        return needsQuoting
            ? string.Concat(quote, field.Replace(quote, quote + quote, StringComparison.Ordinal), quote)
            : field;
    }

    private static string[] ParseCsvLine(string line, CsvProcessorConfig config)
    {
        // Simple CSV parsing - in practice, you might want to use a more robust CSV library.
        // Rows are parsed one line at a time, so a quoted field containing a line break that spans
        // lines (or chunks) is not supported.
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c == config.QuoteCharacter[0])
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == config.QuoteCharacter[0])
                {
                    // Escaped quote
                    current.Append(config.QuoteCharacter);
                    i += 2;
                }
                else
                {
                    // Start or end of quoted field
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else if (c == config.Delimiter[0] && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    private static string ComputeChecksum(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
