using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing JSON/YAML configuration files
/// Interface to be implemented by Phase 2 agents
/// </summary>
public interface IConfigAnalyzerService
{
    /// <summary>
    /// Gets the schema of a configuration file by analyzing its structure and extracting property metadata.
    /// </summary>
    /// <param name="configPath">Path to the configuration file</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Configuration schema containing flattened properties with metadata</returns>
    Task<ConfigSchema> GetConfigSchemaAsync(string configPath, CancellationToken ct = default);

    /// <summary>
    /// Merges multiple configuration files into a single configuration, detecting and reporting conflicts.
    /// </summary>
    /// <param name="configPaths">Array of configuration file paths to merge</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Merged configuration with conflicts and merged settings</returns>
    Task<MergedConfig> MergeConfigsAsync(string[] configPaths, CancellationToken ct = default);

    /// <summary>
    /// Validates configuration consistency against project structure and other configuration files.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validation result with any inconsistencies found</returns>
    Task<ConfigValidationResult> ValidateConfigurationConsistencyAsync(string projectRoot, CancellationToken ct = default);

    /// <summary>
    /// Get all configuration keys from config files in the project root.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of configuration keys found in the project</returns>
    Task<IReadOnlyList<ConfigurationKey>> GetConfigurationKeysAsync(string projectRoot, CancellationToken ct = default);
}
