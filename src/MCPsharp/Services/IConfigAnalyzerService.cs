using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing JSON/YAML configuration files
/// Interface to be implemented by Phase 2 agents
/// </summary>
public interface IConfigAnalyzerService
{
    Task<ConfigSchema> GetConfigSchemaAsync(string configPath);
    Task<MergedConfig> MergeConfigsAsync(string[] configPaths);
}
