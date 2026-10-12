using System.Collections.Generic;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Base interface for stream processors
/// </summary>
public interface IStreamProcessor
{
    StreamProcessorType ProcessorType { get; }
    Task<StreamChunk> ProcessChunkAsync(StreamChunk chunk, Dictionary<string, object> options);
    Task<bool> ValidateOptionsAsync(Dictionary<string, object> options);
}
