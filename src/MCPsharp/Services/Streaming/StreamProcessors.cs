using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Base interface for stream processors
/// </summary>
public interface IStreamProcessor
{
    StreamProcessorType ProcessorType { get; }
    Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options);
    Task<bool> ValidateOptionsAsync(Dictionary<string, object> options);
}

/// <summary>
/// Line-based stream processor for text files
/// </summary>
public class LineStreamProcessor : IStreamProcessor
{
    private readonly ILogger<LineStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.LineProcessor;

    public LineStreamProcessor(ILogger<LineStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<LineStreamProcessor>.Instance;
    }

    public async Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options)
    {
        var config = ParseConfig(options);
        var lines = ExtractLines(chunk, config.Encoding ?? Encoding.UTF8);
        var processedLines = new List<string>();

        foreach (var line in lines)
        {
            var processedLine = ProcessLine(line, config, lines.IndexOf(line) + 1);
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

    public async Task<bool> ValidateOptionsAsync(Dictionary<string, object> options)
    {
        try
        {
            ParseConfig(options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private LineProcessorConfig ParseConfig(Dictionary<string, object> options)
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

    private List<string> ExtractLines(StreamChunk chunk, Encoding encoding)
    {
        var text = encoding.GetString(chunk.Data);
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).ToList();

        // Handle the case where the last line might be incomplete
        if (!chunk.IsLastChunk && lines.Count > 0 && !text.EndsWith(Environment.NewLine))
        {
            // Keep the last line as it might be continued in the next chunk
            return lines;
        }

        return lines;
    }

    private string? ProcessLine(string line, LineProcessorConfig config, long lineNumber = 0)
    {
        var originalLine = line;

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
        if (!string.IsNullOrEmpty(config.LineFilter))
        {
            if (!Regex.IsMatch(line, config.LineFilter))
            {
                return null;
            }
        }

        // Apply line replacement
        if (!string.IsNullOrEmpty(config.LineReplacement))
        {
            line = Regex.Replace(line, config.LineFilter ?? ".*", config.LineReplacement);
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

    private string ComputeChecksum(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Regex-based stream processor
/// </summary>
public class RegexStreamProcessor : IStreamProcessor
{
    private readonly ILogger<RegexStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.RegexProcessor;

    public RegexStreamProcessor(ILogger<RegexStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<RegexStreamProcessor>.Instance;
    }

    public async Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options)
    {
        var config = ParseConfig(options);
        var text = Encoding.UTF8.GetString(chunk.Data);
        var processedText = text;

        if (!string.IsNullOrEmpty(config.Pattern))
        {
            var regexOptions = config.Options;
            if (config.Multiline) regexOptions |= RegexOptions.Multiline;
            if (config.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;

            var regex = new Regex(config.Pattern, regexOptions);

            if (!string.IsNullOrEmpty(config.Replacement))
            {
                processedText = regex.Replace(processedText, config.Replacement, config.MaxMatches);
            }
            else if (config.MatchWholeLine)
            {
                var matches = regex.Matches(processedText);
                var lines = processedText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                var matchingLines = new List<string>();

                foreach (Match match in matches)
                {
                    var lineIndex = processedText.Substring(0, match.Index).Count(c => c == '\n');
                    if (lineIndex < lines.Length)
                    {
                        matchingLines.Add(lines[lineIndex]);
                    }
                }

                processedText = string.Join(Environment.NewLine, matchingLines);
            }
        }

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

    public async Task<bool> ValidateOptionsAsync(Dictionary<string, object> options)
    {
        try
        {
            ParseConfig(options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private RegexProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new RegexProcessorConfig();

        if (options.TryGetValue("Pattern", out var pattern) && pattern is string patternStr)
            config.Pattern = patternStr;

        if (options.TryGetValue("Replacement", out var replacement) && replacement is string replacementStr)
            config.Replacement = replacementStr;

        if (options.TryGetValue("Options", out var opts) && opts is int optionsInt)
            config.Options = (System.Text.RegularExpressions.RegexOptions)optionsInt;

        if (options.TryGetValue("Multiline", out var multiline) && multiline is bool ml)
            config.Multiline = ml;

        if (options.TryGetValue("IgnoreCase", out var ignoreCase) && ignoreCase is bool ic)
            config.IgnoreCase = ic;

        if (options.TryGetValue("MatchWholeLine", out var wholeLine) && wholeLine is bool wl)
            config.MatchWholeLine = wl;

        if (options.TryGetValue("MaxMatches", out var maxMatches) && maxMatches is int mm)
            config.MaxMatches = mm;

        return config;
    }

    private string ComputeChecksum(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// CSV stream processor
/// </summary>
public class CsvStreamProcessor : IStreamProcessor
{
    private readonly ILogger<CsvStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.CsvProcessor;

    public CsvStreamProcessor(ILogger<CsvStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<CsvStreamProcessor>.Instance;
    }

    public async Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options)
    {
        var config = ParseConfig(options);
        var text = Encoding.UTF8.GetString(chunk.Data);
        var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
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

            processedLines.Add(string.Join(config.Delimiter, fields));
        }

        var processedText = string.Join(Environment.NewLine, processedLines);
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

    public async Task<bool> ValidateOptionsAsync(Dictionary<string, object> options)
    {
        try
        {
            ParseConfig(options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private CsvProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new CsvProcessorConfig();

        if (options.TryGetValue("Delimiter", out var delimiter) && delimiter is string delimStr)
            config.Delimiter = delimStr;

        if (options.TryGetValue("HasHeader", out var hasHeader) && hasHeader is bool hh)
            config.HasHeader = hh;

        if (options.TryGetValue("QuoteCharacter", out var quote) && quote is string quoteStr)
            config.QuoteCharacter = quoteStr;

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

    private string[] ParseCsvLine(string line, CsvProcessorConfig config)
    {
        // Simple CSV parsing - in practice, you might want to use a more robust CSV library
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

    private string ComputeChecksum(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Binary stream processor
/// </summary>
public class BinaryStreamProcessor : IStreamProcessor
{
    private readonly ILogger<BinaryStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.BinaryProcessor;

    public BinaryStreamProcessor(ILogger<BinaryStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<BinaryStreamProcessor>.Instance;
    }

    public async Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options)
    {
        var config = ParseConfig(options);
        var data = new List<byte>(chunk.Data);

        // Apply search and replace if configured
        if (config.SearchPattern != null && config.ReplacementPattern != null)
        {
            data = ApplySearchReplace(chunk.Data, config.SearchPattern, config.ReplacementPattern);
        }

        // Calculate checksum if configured
        string? checksum = null;
        if (config.CalculateChecksums)
        {
            checksum = ComputeChecksum(data.ToArray(), config.ChecksumAlgorithm);
        }

        // Add custom metadata
        var metadata = new Dictionary<string, object>(chunk.Metadata);
        foreach (var kvp in config.CustomMetadata)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        return new StreamChunk
        {
            Data = data.ToArray(),
            Position = chunk.Position,
            Length = data.Count,
            ChunkIndex = chunk.ChunkIndex,
            IsLastChunk = chunk.IsLastChunk,
            Metadata = metadata,
            Checksum = checksum
        };
    }

    public async Task<bool> ValidateOptionsAsync(Dictionary<string, object> options)
    {
        try
        {
            ParseConfig(options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private BinaryProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new BinaryProcessorConfig();

        if (options.TryGetValue("SearchPattern", out var search) && search is byte[] searchBytes)
            config.SearchPattern = searchBytes;

        if (options.TryGetValue("ReplacementPattern", out var replace) && replace is byte[] replaceBytes)
            config.ReplacementPattern = replaceBytes;

        if (options.TryGetValue("CalculateChecksums", out var checksum) && checksum is bool cs)
            config.CalculateChecksums = cs;

        if (options.TryGetValue("ChecksumAlgorithm", out var algorithm) && algorithm is string algoStr)
            config.ChecksumAlgorithm = algoStr;

        if (options.TryGetValue("PreserveTimestamps", out var timestamps) && timestamps is bool pt)
            config.PreserveTimestamps = pt;

        if (options.TryGetValue("CustomMetadata", out var metadata) && metadata is Dictionary<string, object> metaDict)
            config.CustomMetadata = metaDict;

        return config;
    }

    private List<byte> ApplySearchReplace(byte[] data, byte[] searchPattern, byte[] replacementPattern)
    {
        var result = new List<byte>();
        var i = 0;

        while (i <= data.Length - searchPattern.Length)
        {
            var found = true;
            for (int j = 0; j < searchPattern.Length; j++)
            {
                if (data[i + j] != searchPattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                result.AddRange(replacementPattern);
                i += searchPattern.Length;
            }
            else
            {
                result.Add(data[i]);
                i++;
            }
        }

        // Add remaining bytes
        while (i < data.Length)
        {
            result.Add(data[i]);
            i++;
        }

        return result;
    }

    private string ComputeChecksum(byte[] data, string algorithm)
    {
        using var hashAlgorithm = algorithm.ToUpperInvariant() switch
        {
            "SHA256" or "SHA-256" => SHA256.Create(),
            "SHA512" or "SHA-512" => SHA512.Create(),
            "SHA1" or "SHA-1" => SHA1.Create(),
            "MD5" => MD5.Create(),
            _ => (System.Security.Cryptography.HashAlgorithm)SHA256.Create()
        };

        var hash = hashAlgorithm.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}