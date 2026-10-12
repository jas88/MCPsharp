using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Binary stream processor
/// </summary>
public partial class BinaryStreamProcessor : IStreamProcessor
{
    private readonly ILogger<BinaryStreamProcessor> _logger;

    public StreamProcessorType ProcessorType => StreamProcessorType.BinaryProcessor;

    public BinaryStreamProcessor(ILogger<BinaryStreamProcessor>? logger = null)
    {
        _logger = logger ?? NullLogger<BinaryStreamProcessor>.Instance;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid binary processor options")]
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

    private static BinaryProcessorConfig ParseConfig(Dictionary<string, object> options)
    {
        var config = new BinaryProcessorConfig();

        if (options.TryGetValue("SearchPattern", out var search) && search is byte[] searchBytes)
            config.SearchPattern = searchBytes;

        if (options.TryGetValue("ReplacementPattern", out var replace) && replace is byte[] replaceBytes)
            config.ReplacementPattern = replaceBytes;

        if (options.TryGetValue("CalculateChecksums", out var checksum) && checksum is bool cs)
            config.CalculateChecksums = cs;

        if (options.TryGetValue("ChecksumAlgorithm", out var algorithm))
            config.ChecksumAlgorithm = NormalizeChecksumAlgorithm(algorithm);

        if (options.TryGetValue("PreserveTimestamps", out var timestamps) && timestamps is bool pt)
            config.PreserveTimestamps = pt;

        if (options.TryGetValue("CustomMetadata", out var metadata) && metadata is Dictionary<string, object> metaDict)
            config.CustomMetadata = metaDict;

        return config;
    }

    private static List<byte> ApplySearchReplace(byte[] data, byte[] searchPattern, byte[] replacementPattern)
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

    private static string NormalizeChecksumAlgorithm(object? algorithm)
    {
        return algorithm is string name
            ? name.ToUpperInvariant() switch
            {
                "SHA256" or "SHA-256" => "SHA256",
                "SHA512" or "SHA-512" => "SHA512",
                _ => throw new ArgumentException(
                    $"Unsupported checksum algorithm '{name}'. Supported algorithms: SHA256, SHA512.",
                    nameof(algorithm))
            }
            : throw new ArgumentException(
                "ChecksumAlgorithm must be a string naming a supported algorithm (SHA256, SHA512).",
                nameof(algorithm));
    }

    private static string ComputeChecksum(byte[] data, string algorithm)
    {
        var hash = NormalizeChecksumAlgorithm(algorithm) switch
        {
            "SHA512" => SHA512.HashData(data),
            _ => SHA256.HashData(data)
        };

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
