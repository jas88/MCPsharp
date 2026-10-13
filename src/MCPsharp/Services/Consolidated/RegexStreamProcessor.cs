using System.Text;
using MCPsharp.Models.Consolidated;
using StreamingRegexProcessor = MCPsharp.Services.Streaming.RegexStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Regex processor: applies a pattern/replacement (or keeps matching lines) using
/// <see cref="StreamingRegexProcessor"/>. Parameters: Pattern, Replacement, IgnoreCase, Multiline,
/// MatchWholeLine, MaxMatches (applied per chunk; 0 or absent = unlimited), Options.
/// Chunks end on line boundaries, so matches never span chunks.
/// </summary>
public class RegexStreamProcessor : IStreamProcessor
{
    private readonly StreamingRegexProcessor _processor = new();

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("regex", () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);

            // Compile the pattern up front so a malformed one fails even when the input is empty
            StreamingRegexProcessor.ValidatePattern(options);
            var inputEncoding = StreamProcessorOptionMapper.ResolveEncoding(context.Options);
            return StreamingProcessorAdapter.ProcessLinesAsync(_processor, input, output, context, options, inputEncoding, Encoding.UTF8, ct);
        });

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv", "json", "xml", "html" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text", "json", "xml", "html" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { "Pattern", "Replacement", "IgnoreCase", "Multiline", "MatchWholeLine", "MaxMatches", "Options" }
        });
}
