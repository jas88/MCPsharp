using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Generic transform processor. No transformation semantics are defined for it yet, so it always
/// reports failure instead of claiming success without touching the input.
/// </summary>
public class TransformStreamProcessor : IStreamProcessor
{
    internal const string NotSupportedMessage =
        "The 'transform' processor is not supported yet: no transformation is defined for it. " +
        "Use 'line', 'regex', 'csv', 'json', 'xml', 'log' or 'binary' instead.";

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(StreamProcessorExecution.Failure(NotSupportedMessage));

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string>(),
            SupportedOutputFormats = new List<string>(),
            SupportsStreaming = false,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = 0
        });
}
