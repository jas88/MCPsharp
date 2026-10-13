using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Models.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Regex-based stream processor
/// </summary>
public partial class RegexStreamProcessor : IStreamProcessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger<RegexStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.RegexProcessor;

    public RegexStreamProcessor(ILogger<RegexStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<RegexStreamProcessor>.Instance;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid regex processor options")]
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
        var processedText = text;

        if (!string.IsNullOrEmpty(config.Pattern))
        {
            var regexOptions = config.Options;
            if (config.Multiline)
                regexOptions |= RegexOptions.Multiline;
            if (config.IgnoreCase)
                regexOptions |= RegexOptions.IgnoreCase;

            var regex = new Regex(config.Pattern, regexOptions, RegexTimeout);

            // An empty replacement deletes the matches; only a missing one means "no replacement"
            if (config.Replacement != null)
            {
                // MaxMatches <= 0 means unlimited; Regex.Replace only treats a negative count as unlimited
                // (a count of 0 replaces nothing)
                var count = config.MaxMatches > 0 ? config.MaxMatches : -1;
                processedText = regex.Replace(processedText, config.Replacement, count);
            }
            else if (config.MatchWholeLine)
            {
                // Keep each line that contains a match, once, in its original order
                var lines = TextChunkLines.Split(processedText, out var endsWithLineBreak);
                var matchingLines = lines.Where(line => regex.IsMatch(line)).ToList();
                processedText = TextChunkLines.Join(matchingLines, endsWithLineBreak);
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

    private static RegexProcessorConfig ParseConfig(Dictionary<string, object> options)
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

    private static string ComputeChecksum(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
