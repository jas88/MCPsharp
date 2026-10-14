using MCPsharp.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services;

/// <summary>
/// Implementation of IConfigAnalyzerService for analyzing configuration files
/// </summary>
public partial class ConfigAnalyzerService : IConfigAnalyzerService
{
    private readonly ILogger<ConfigAnalyzerService> _logger;
    private readonly IDeserializer _yamlDeserializer;

    // Keys that commonly contain sensitive information
    private static readonly HashSet<string> SensitiveKeyPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "key", "connectionstring", "apikey", "api_key",
        "privatekey", "private_key", "certificate", "credentials", "auth"
    };

    public ConfigAnalyzerService(ILogger<ConfigAnalyzerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Gets the schema of a configuration file by analyzing its structure and extracting property metadata.
    /// </summary>
    /// <param name="configPath">Path to the configuration file</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Configuration schema containing flattened properties with metadata</returns>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configuration file cannot be parsed</exception>
    public async Task<ConfigSchema> GetConfigSchemaAsync(string configPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Configuration path cannot be null or empty", nameof(configPath));
        }

        var absolutePath = Path.GetFullPath(configPath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {absolutePath}");
        }

        LogAnalyzingConfigurationSchemaForFile(_logger, absolutePath);

        try
        {
            var content = await File.ReadAllTextAsync(absolutePath, ct);
            var fileExtension = Path.GetExtension(absolutePath).ToLowerInvariant();

            Dictionary<string, object> configData = fileExtension switch
            {
                ".json" => await ParseJsonConfigAsync(content, absolutePath),
                ".yml" or ".yaml" => await ParseYamlConfigAsync(content, absolutePath),
                _ => throw new NotSupportedException($"Configuration file format '{fileExtension}' is not supported")
            };

            var properties = ExtractConfigProperties(configData);

            LogSuccessfullyAnalyzedConfigurationSchemaFor(_logger, absolutePath, properties.Count);

            return new ConfigSchema
            {
                FilePath = absolutePath,
                Properties = properties
            };
        }
        catch (Exception ex) when (!(ex is FileNotFoundException || ex is InvalidOperationException || ex is NotSupportedException))
        {
            LogFailedToAnalyzeConfigurationSchema(_logger, ex, absolutePath);
            throw new InvalidOperationException($"Failed to analyze configuration file: {absolutePath}", ex);
        }
    }

    /// <summary>
    /// Merges multiple configuration files into a single configuration, detecting and reporting conflicts.
    /// </summary>
    /// <param name="configPaths">Array of configuration file paths to merge</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Merged configuration with conflicts and merged settings</returns>
    /// <exception cref="ArgumentException">Thrown when configPaths is null or empty</exception>
    public async Task<MergedConfig> MergeConfigsAsync(string[] configPaths, CancellationToken ct = default)
    {
        if (configPaths == null || configPaths.Length == 0)
        {
            throw new ArgumentException("At least one configuration file path must be provided", nameof(configPaths));
        }

        LogMergingConfigurationFiles(_logger, configPaths.Length);

        var mergedSettings = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<ConfigConflict>();
        var sourceFiles = new List<string>();

        foreach (var configPath in configPaths)
        {
            try
            {
                var absolutePath = Path.GetFullPath(configPath);

                if (!File.Exists(absolutePath))
                {
                    LogConfigurationFileNotFoundSkipping(_logger, absolutePath);
                    continue;
                }

                var content = await File.ReadAllTextAsync(absolutePath, ct);
                var fileExtension = Path.GetExtension(absolutePath).ToLowerInvariant();

                Dictionary<string, object> configData = fileExtension switch
                {
                    ".json" => await ParseJsonConfigAsync(content, absolutePath),
                    ".yml" or ".yaml" => await ParseYamlConfigAsync(content, absolutePath),
                    _ => throw new NotSupportedException($"Configuration file format '{fileExtension}' is not supported")
                };

                // Flatten the configuration data for merging
                var flatConfig = FlattenConfig(configData);

                foreach (var kvp in flatConfig)
                {
                    if (mergedSettings.TryGetValue(kvp.Key, out var existingValue))
                    {
                        // Conflict detected - later configs override earlier ones
                        if (!AreValuesEqual(existingValue, kvp.Value))
                        {
                            conflicts.Add(new ConfigConflict
                            {
                                Key = kvp.Key,
                                File1 = sourceFiles.LastOrDefault(f => ContainsConfigKey(f, kvp.Key)) ?? "Unknown",
                                Value1 = existingValue,
                                File2 = absolutePath,
                                Value2 = kvp.Value
                            });

                            LogConfigurationConflictDetectedForKey(_logger, kvp.Key);
                        }

                        // Always update to the latest value (later configs override earlier ones)
                        mergedSettings[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        mergedSettings[kvp.Key] = kvp.Value;
                    }
                }

                sourceFiles.Add(absolutePath);
                LogSuccessfullyProcessedConfigurationFile(_logger, absolutePath);
            }
            catch (Exception ex)
            {
                LogFailedToProcessConfigurationFile(_logger, ex, configPath);
                throw;
            }
        }

        LogSuccessfullyMergedConfigurationFilesWith(_logger, sourceFiles.Count, conflicts.Count);

        return new MergedConfig
        {
            SourceFiles = sourceFiles,
            MergedSettings = mergedSettings,
            Conflicts = conflicts.Count > 0 ? conflicts : null
        };
    }

    /// <summary>
    /// Validates configuration consistency against project structure and other configuration files.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validation result with any inconsistencies found</returns>
    public async Task<ConfigValidationResult> ValidateConfigurationConsistencyAsync(string projectRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root path cannot be null or empty", nameof(projectRoot));
        }

        LogValidatingConfigurationConsistencyForProject(_logger, projectRoot);

        ConfigValidationResult result;

        try
        {
            // Find all configuration files
            var configFiles = await FindConfigurationFilesAsync(projectRoot);

            result = new ConfigValidationResult
            {
                ProjectRoot = projectRoot,
                ConfigurationFiles = configFiles,
                Issues = new List<ConfigValidationIssue>()
            };

            // Validate each configuration file
            foreach (var configFile in configFiles)
            {
                try
                {
                    var schema = await GetConfigSchemaAsync(configFile, ct);
                    await ValidateSingleConfigurationFileAsync(configFile, schema, result);
                }
                catch (Exception ex)
                {
                    LogConfigFileValidationFailed(_logger, ex, configFile);
                    result.Issues.Add(new ConfigValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Failed to analyze configuration file: {ex.Message}",
                        FilePath = configFile
                    });
                }
            }

            // Check for cross-file consistency
            await ValidateCrossFileConsistencyAsync(configFiles, result, ct);

            LogConfigurationValidationCompletedWithIssues(_logger, result.Issues.Count);
        }
        catch (Exception ex)
        {
            LogFailedToValidateConfigurationConsistency(_logger, ex, projectRoot);

            // Return a result with the error information if result wasn't initialized
            return new ConfigValidationResult
            {
                ProjectRoot = projectRoot,
                ConfigurationFiles = new List<string>(),
                Issues = new List<ConfigValidationIssue>
                {
                    new ConfigValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Failed to validate configuration: {ex.Message}",
                        FilePath = projectRoot
                    }
                }
            };
        }

        return result;
    }

    /// <summary>
    /// Get all configuration keys from config files in the project root
    /// </summary>
    public async Task<IReadOnlyList<ConfigurationKey>> GetConfigurationKeysAsync(
        string projectRoot,
        CancellationToken ct = default)
    {
        var keys = new List<ConfigurationKey>();

        // Find all JSON config files
        var configFiles = Directory.EnumerateFiles(projectRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") &&
                       !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .ToList();

        foreach (var configFile in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(configFile, ct);
                var jsonKeys = ExtractJsonKeys(content, configFile);
                keys.AddRange(jsonKeys);
            }
            catch (Exception ex)
            {
                // Skip files that can't be read
                LogSkippedUnreadableConfigFile(_logger, ex, configFile);
            }
        }

        return keys;
    }
}
