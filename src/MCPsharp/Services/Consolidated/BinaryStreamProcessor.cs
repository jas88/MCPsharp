using MCPsharp.Models.Consolidated;
using StreamingBinaryProcessor = MCPsharp.Services.Streaming.BinaryStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Binary processor: copies bytes, optionally replacing a byte pattern and computing per-chunk
/// checksums, using <see cref="StreamingBinaryProcessor"/>. Parameters: SearchPattern and
/// ReplacementPattern (supplied together, each a string taken as UTF-8 or an array of byte values 0-255),
/// CalculateChecksums, ChecksumAlgorithm.
/// </summary>
public class BinaryStreamProcessor : IStreamProcessor
{
    private const string SearchPatternKey = "SearchPattern";
    private const string ReplacementPatternKey = "ReplacementPattern";

    private readonly StreamingBinaryProcessor _processor = new();

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("binary", () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
            var hasSearch = options.ContainsKey(SearchPatternKey);
            var hasReplacement = options.ContainsKey(ReplacementPatternKey);
            if (hasSearch != hasReplacement)
            {
                return Task.FromResult(StreamProcessorExecution.Failure(
                    "The 'binary' processor failed: SearchPattern and ReplacementPattern must be supplied together"));
            }

            StreamProcessorOptionMapper.ConvertToBytes(options, SearchPatternKey);
            StreamProcessorOptionMapper.ConvertToBytes(options, ReplacementPatternKey);

            var search = options.TryGetValue(SearchPatternKey, out var value) ? (byte[])value : null;
            if (search is { Length: 0 })
            {
                return Task.FromResult(StreamProcessorExecution.Failure(
                    "The 'binary' processor failed: SearchPattern must not be empty"));
            }

            return StreamingProcessorAdapter.ProcessBytesAsync(_processor, input, output, context, options, search, ct);
        });

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "bin", "dat", "exe", "dll", "so", "dylib" },
            SupportedOutputFormats = new List<string> { "bin", "dat" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { SearchPatternKey, ReplacementPatternKey, "CalculateChecksums", "ChecksumAlgorithm" }
        });
}
