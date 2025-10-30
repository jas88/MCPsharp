using MCPsharp.Models;

namespace MCPsharp.Services.Phase2;

/// <summary>
/// Stub implementation of FeatureTracerService for Phase 2
/// To be replaced by actual implementation
/// </summary>
public class FeatureTracerService : IFeatureTracerService
{
    public Task<FeatureMap> TraceFeatureAsync(string featureName)
    {
        throw new NotImplementedException("FeatureTracerService.TraceFeatureAsync not yet implemented");
    }

    public Task<FeatureMap> DiscoverFeatureComponentsAsync(string entryPoint)
    {
        throw new NotImplementedException("FeatureTracerService.DiscoverFeatureComponentsAsync not yet implemented");
    }
}
