using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for tracing features across multiple files
/// Interface to be implemented by Phase 2 agents
/// </summary>
public interface IFeatureTracerService
{
    Task<FeatureMap> TraceFeatureAsync(string featureName);
    Task<FeatureMap> DiscoverFeatureComponentsAsync(string entryPoint);
}
