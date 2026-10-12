using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public class RegexStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv", "json", "xml", "html" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text", "json", "xml", "html" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue
        });
}
