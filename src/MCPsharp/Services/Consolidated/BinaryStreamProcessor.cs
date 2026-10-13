using MCPsharp.Models.Consolidated;
using StreamingBinaryProcessor = MCPsharp.Services.Streaming.BinaryStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Binary processor: copies bytes, optionally replacing a byte pattern and computing per-chunk
/// checksums, using <see cref="StreamingBinaryProcessor"/>. Parameters: SearchPattern and
/// ReplacementPattern (byte arrays, or strings taken as UTF-8), CalculateChecksums, ChecksumAlgorithm.
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
            StreamProcessorOptionMapper.ConvertStringToBytes(options, SearchPatternKey);
            StreamProcessorOptionMapper.ConvertStringToBytes(options, ReplacementPatternKey);

            options.TryGetValue(SearchPatternKey, out var search);
            if (search is byte[] { Length: 0 })
            {
                return Task.FromResult(StreamProcessorExecution.Failure(
                    "The 'binary' processor failed: SearchPattern must not be empty"));
            }

            var boundaryPattern = options.ContainsKey(ReplacementPatternKey) ? search as byte[] : null;
            return StreamingProcessorAdapter.ProcessBytesAsync(_processor, input, output, context, options, boundaryPattern, ct);
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
