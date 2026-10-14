using MCPsharp.Models;

namespace MCPsharp.Services;

public partial class ConfigAnalyzerService
{
    /// <summary>
    /// Finds all configuration files in the project.
    /// </summary>
    private Task<IReadOnlyList<string>> FindConfigurationFilesAsync(string projectRoot)
    {
        var configFiles = new List<string>();
        var searchPatterns = new[] { "*.json", "*.yml", "*.yaml" };

        foreach (var pattern in searchPatterns)
        {
            var files = Directory.EnumerateFiles(projectRoot, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") &&
                           !f.Contains("\\bin\\") && !f.Contains("\\obj\\") &&
                           !Path.GetDirectoryName(f)!.Contains("node_modules"))
                .ToList();

            configFiles.AddRange(files);
        }

        LogFoundConfigurationFiles(_logger, configFiles.Count);
        return Task.FromResult<IReadOnlyList<string>>(configFiles.Distinct().ToList());
    }

    /// <summary>
    /// Validates a single configuration file for common issues.
    /// </summary>
    private static Task ValidateSingleConfigurationFileAsync(string filePath, ConfigSchema schema, ConfigValidationResult result)
    {
        // Check for empty configuration files
        if (schema.Properties.Count == 0)
        {
            result.Issues.Add(new ConfigValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = "Configuration file is empty or contains no properties",
                FilePath = filePath
            });
        }

        // Check for sensitive values in configuration
        var sensitiveProperties = schema.Properties.Where(p => p.Value.IsSensitive).ToList();
        if (sensitiveProperties.Count > 0)
        {
            result.Issues.Add(new ConfigValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Configuration file contains {sensitiveProperties.Count} sensitive properties that should be secured",
                FilePath = filePath,
                Details = sensitiveProperties.Select(p => p.Key).ToList()
            });
        }

        // Check for common configuration patterns
        return ValidateCommonConfigurationPatternsAsync(filePath, schema, result);
    }

    /// <summary>
    /// Validates common configuration patterns and best practices.
    /// </summary>
    private static Task ValidateCommonConfigurationPatternsAsync(string filePath, ConfigSchema schema, ConfigValidationResult result)
    {
        // Check for connection strings without encryption
        var connectionStringProps = schema.Properties
            .Where(p => p.Key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var prop in connectionStringProps)
        {
            var value = prop.Value.DefaultValue?.ToString();
            if (!string.IsNullOrEmpty(value) && !value.Contains("Encrypt=", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ConfigValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Connection string should specify encryption: {prop.Key}",
                    FilePath = filePath,
                    Details = new List<string> { prop.Key }
                });
            }
        }

        // Check for logging configuration
        if (!schema.Properties.Any(p => p.Key.Contains("logging", StringComparison.OrdinalIgnoreCase)))
        {
            result.Issues.Add(new ConfigValidationIssue
            {
                Severity = ValidationSeverity.Info,
                Message = "Configuration file does not contain logging settings",
                FilePath = filePath
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates consistency across multiple configuration files.
    /// </summary>
    private async Task ValidateCrossFileConsistencyAsync(IReadOnlyList<string> configFiles, ConfigValidationResult result, CancellationToken ct)
    {
        if (configFiles.Count < 2)
            return;

        try
        {
            var mergedConfig = await MergeConfigsAsync(configFiles.ToArray(), ct);

            // Check for conflicts that were detected during merging
            if (mergedConfig.Conflicts?.Any() == true)
            {
                foreach (var conflict in mergedConfig.Conflicts)
                {
                    result.Issues.Add(new ConfigValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Configuration conflict for key '{conflict.Key}' between files",
                        FilePath = conflict.File1,
                        Details = new List<string>
                        {
                            $"Conflict with: {conflict.File2}",
                            $"Value 1: {conflict.Value1}",
                            $"Value 2: {conflict.Value2}"
                        }
                    });
                }
            }

            // Check for environment-specific inconsistencies
            await ValidateEnvironmentSpecificConsistencyAsync(configFiles, result, ct);
        }
        catch (Exception ex)
        {
            LogCrossFileValidationFailed(_logger, ex);
            result.Issues.Add(new ConfigValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Failed to validate cross-file consistency: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Validates environment-specific configuration consistency.
    /// </summary>
    private async Task ValidateEnvironmentSpecificConsistencyAsync(IReadOnlyList<string> configFiles, ConfigValidationResult result, CancellationToken ct)
    {
        var environmentPatterns = new[] { "development", "staging", "production", "testing" };

        foreach (var env in environmentPatterns)
        {
            var envConfigs = configFiles.Where(f =>
                Path.GetFileNameWithoutExtension(f).Contains(env, StringComparison.OrdinalIgnoreCase)).ToList();

            if (envConfigs.Count > 1)
            {
                // Check for consistency within the same environment
                try
                {
                    var mergedEnvConfig = await MergeConfigsAsync(envConfigs.ToArray(), ct);
                    if (mergedEnvConfig.Conflicts?.Any() == true)
                    {
                        foreach (var conflict in mergedEnvConfig.Conflicts)
                        {
                            result.Issues.Add(new ConfigValidationIssue
                            {
                                Severity = ValidationSeverity.Warning,
                                Message = $"Environment '{env}' has conflicting configuration values",
                                Details = new List<string>
                                {
                                    $"Key: {conflict.Key}",
                                    $"File 1: {conflict.File1}",
                                    $"File 2: {conflict.File2}"
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogFailedToValidateEnvironmentConsistency(_logger, ex, env);
                }
            }
        }
    }
}
