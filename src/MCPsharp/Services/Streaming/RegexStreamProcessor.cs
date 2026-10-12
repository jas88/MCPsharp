using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Streaming;

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
            if (config.Multiline) regexOptions |= RegexOptions.Multiline;
            if (config.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;

            var regex = new Regex(config.Pattern, regexOptions, RegexTimeout);

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
