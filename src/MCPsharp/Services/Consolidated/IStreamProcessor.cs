using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Stream processor interface
/// </summary>
public interface IStreamProcessor
{
    Task InitializeAsync(ProcessorOptions options, CancellationToken ct);
    Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct);
    Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct);
}
