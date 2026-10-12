using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Built-in stream processors
/// </summary>
public class LineStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue
        });
}
