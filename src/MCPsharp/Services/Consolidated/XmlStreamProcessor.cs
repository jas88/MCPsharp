using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public class XmlStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "xml", "xhtml", "html", "svg", "config" },
            SupportedOutputFormats = new List<string> { "xml", "xhtml", "html", "json", "txt" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}
