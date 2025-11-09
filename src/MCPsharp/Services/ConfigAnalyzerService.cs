using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services;

/// <summary>
/// Implementation of IConfigAnalyzerService for analyzing configuration files
/// </summary>
public class ConfigAnalyzerService : IConfigAnalyzerService
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

        _logger.LogDebug("Analyzing configuration schema for file: {FilePath}", absolutePath);

        try
        {
            var content = await File.ReadAllTextAsync(absolutePath, ct);
            var fileExtension = Path.GetExtension(absolutePath).ToLowerInvariant();

            Dictionary<string, object> configData = fileExtension switch
            {
                ".json" => await ParseJsonConfigAsync(content, absolutePath, ct),
                ".yml" or ".yaml" => await ParseYamlConfigAsync(content, absolutePath, ct),
                _ => throw new NotSupportedException($"Configuration file format '{fileExtension}' is not supported")
            };

            var properties = ExtractConfigProperties(configData, absolutePath);

            _logger.LogInformation("Successfully analyzed configuration schema for {FilePath}, found {PropertyCount} properties",
                absolutePath, properties.Count);

            return new ConfigSchema
            {
                FilePath = absolutePath,
                Properties = properties
            };
        }
        catch (Exception ex) when (!(ex is FileNotFoundException || ex is InvalidOperationException || ex is NotSupportedException))
        {
            _logger.LogError(ex, "Failed to analyze configuration schema for file: {FilePath}", absolutePath);
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

        _logger.LogDebug("Merging {ConfigCount} configuration files", configPaths.Length);

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
                    _logger.LogWarning("Configuration file not found, skipping: {FilePath}", absolutePath);
                    continue;
                }

                var content = await File.ReadAllTextAsync(absolutePath, ct);
                var fileExtension = Path.GetExtension(absolutePath).ToLowerInvariant();

                Dictionary<string, object> configData = fileExtension switch
                {
                    ".json" => await ParseJsonConfigAsync(content, absolutePath, ct),
                    ".yml" or ".yaml" => await ParseYamlConfigAsync(content, absolutePath, ct),
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

                            _logger.LogDebug("Configuration conflict detected for key '{Key}' between files", kvp.Key);
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
                _logger.LogDebug("Successfully processed configuration file: {FilePath}", absolutePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process configuration file: {FilePath}", configPath);
                throw;
            }
        }

        _logger.LogInformation("Successfully merged {ConfigCount} configuration files with {ConflictCount} conflicts",
            sourceFiles.Count, conflicts.Count);

        return new MergedConfig
        {
            SourceFiles = sourceFiles,
            MergedSettings = mergedSettings,
            Conflicts = conflicts.Any() ? conflicts : null
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

        _logger.LogDebug("Validating configuration consistency for project: {ProjectRoot}", projectRoot);

        ConfigValidationResult result;

        try
        {
            // Find all configuration files
            var configFiles = await FindConfigurationFilesAsync(projectRoot, ct);

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
                    await ValidateSingleConfigurationFileAsync(configFile, schema, result, ct);
                }
                catch (Exception ex)
                {
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

            _logger.LogInformation("Configuration validation completed with {IssueCount} issues found", result.Issues.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate configuration consistency for project: {ProjectRoot}", projectRoot);

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
            catch
            {
                // Skip files that can't be read
            }
        }

        return keys;
    }

    private List<ConfigurationKey> ExtractJsonKeys(string json, string filePath)
    {
        var keys = new List<ConfigurationKey>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            ExtractKeysRecursive(doc.RootElement, "", filePath, keys);
        }
        catch
        {
            // Skip invalid JSON
        }

        return keys;
    }

    private void ExtractKeysRecursive(
        JsonElement element,
        string prefix,
        string filePath,
        List<ConfigurationKey> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : $"{prefix}:{property.Name}";

                keys.Add(new ConfigurationKey
                {
                    Key = key,
                    FilePath = filePath,
                    Value = property.Value.ToString()
                });

                ExtractKeysRecursive(property.Value, key, filePath, keys);
            }
        }
    }

    /// <summary>
    /// Parses a JSON configuration file into a dictionary.
    /// </summary>
    private Task<Dictionary<string, object>> ParseJsonConfigAsync(string content, string filePath, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Parsing JSON configuration file: {FilePath}", filePath);

            using var document = JsonDocument.Parse(content);
            var result = new Dictionary<string, object>();

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                ConvertJsonElementToDictionary(document.RootElement, result, "");
            }

            _logger.LogDebug("Successfully parsed JSON configuration with {RootPropertyCount} root properties",
                result.Count);

            return Task.FromResult(result);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON configuration file: {FilePath}", filePath);
            throw new InvalidOperationException($"Invalid JSON in configuration file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Parses a YAML configuration file into a dictionary.
    /// </summary>
    private Task<Dictionary<string, object>> ParseYamlConfigAsync(string content, string filePath, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Parsing YAML configuration file: {FilePath}", filePath);

            var yamlObject = _yamlDeserializer.Deserialize<Dictionary<string, object>>(content);
            var result = yamlObject ?? new Dictionary<string, object>();

            _logger.LogDebug("Successfully parsed YAML configuration with {RootPropertyCount} root properties",
                result.Count);

            return Task.FromResult(result);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            _logger.LogError(ex, "Failed to parse YAML configuration file: {FilePath}", filePath);
            throw new InvalidOperationException($"Invalid YAML in configuration file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Converts a JsonElement to a dictionary recursively.
    /// </summary>
    private void ConvertJsonElementToDictionary(JsonElement element, Dictionary<string, object> target, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}:{property.Name}";

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Object:
                        var nestedDict = new Dictionary<string, object>();
                        ConvertJsonElementToDictionary(property.Value, nestedDict, key);
                        target[key] = nestedDict;
                        break;

                    case JsonValueKind.Array:
                        var array = new List<object>();
                        foreach (var arrayItem in property.Value.EnumerateArray())
                        {
                            array.Add(ConvertJsonElementToObject(arrayItem));
                        }
                        target[key] = array;
                        break;

                    case JsonValueKind.String:
                        target[key] = property.Value.GetString() ?? string.Empty;
                        break;

                    case JsonValueKind.Number:
                        if (property.Value.TryGetInt32(out var intValue))
                            target[key] = intValue;
                        else if (property.Value.TryGetInt64(out var longValue))
                            target[key] = longValue;
                        else if (property.Value.TryGetDouble(out var doubleValue))
                            target[key] = doubleValue;
                        else
                            target[key] = property.Value.GetDecimal();
                        break;

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        target[key] = property.Value.GetBoolean();
                        break;

                    case JsonValueKind.Null:
                        target[key] = null!;
                        break;

                    default:
                        target[key] = property.Value.ToString();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Converts a JsonElement to an object.
    /// </summary>
    private object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? (object)intValue :
                                  element.TryGetInt64(out var longValue) ? (object)longValue :
                                  element.TryGetDouble(out var doubleValue) ? (object)doubleValue :
                                  element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            JsonValueKind.Object => ConvertJsonElementToObjectDictionary(element),
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Converts a JsonElement object to a Dictionary.
    /// </summary>
    private Dictionary<string, object> ConvertJsonElementToObjectDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        ConvertJsonElementToDictionary(element, dict, "");
        return dict;
    }

    /// <summary>
    /// Extracts configuration properties with metadata from a configuration dictionary.
    /// </summary>
    private Dictionary<string, ConfigProperty> ExtractConfigProperties(Dictionary<string, object> configData, string filePath)
    {
        var properties = new Dictionary<string, ConfigProperty>();
        var flatConfig = FlattenConfig(configData);

        foreach (var kvp in flatConfig)
        {
            properties[kvp.Key] = new ConfigProperty
            {
                Path = kvp.Key,
                Type = GetPropertyType(kvp.Value),
                DefaultValue = kvp.Value,
                IsSensitive = IsSensitiveKey(kvp.Key)
            };
        }

        return properties;
    }

    /// <summary>
    /// Flattens a nested configuration dictionary into dot-notation paths.
    /// </summary>
    private Dictionary<string, object> FlattenConfig(Dictionary<string, object> config, string prefix = "")
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in config)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";

            if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                var nestedResult = FlattenConfig(nestedDict, key);
                foreach (var nestedKvp in nestedResult)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Determines the type of a configuration property value.
    /// </summary>
    private static string GetPropertyType(object value)
    {
        return value switch
        {
            null => "null",
            string => "string",
            int or long or short or byte => "integer",
            float or double or decimal => "number",
            bool => "boolean",
            Dictionary<string, object> => "object",
            List<object> => "array",
            _ => value.GetType().Name.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Determines if a configuration key is likely to contain sensitive information.
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        var keyParts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return keyParts.Any(part =>
            SensitiveKeyPatterns.Any(pattern =>
                part.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Compares two configuration values for equality.
    /// </summary>
    private static bool AreValuesEqual(object value1, object value2)
    {
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;

        // Convert both to string for comparison to handle type differences
        return string.Equals(value1.ToString(), value2.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a configuration file contains a specific key (helper for conflict detection).
    /// </summary>
    private static bool ContainsConfigKey(string filePath, string key)
    {
        try
        {
            // This is a simplified check - in a real implementation, you might want to cache the parsed configs
            var content = File.ReadAllText(filePath);
            var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

            return fileExtension switch
            {
                ".json" => content.Contains($"\"{key.Replace(":", "\":\"")}", StringComparison.OrdinalIgnoreCase),
                ".yml" or ".yaml" => content.Contains(key.Replace(":", ":"), StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds all configuration files in the project.
    /// </summary>
    private Task<IReadOnlyList<string>> FindConfigurationFilesAsync(string projectRoot, CancellationToken ct)
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

        _logger.LogDebug("Found {ConfigFileCount} configuration files", configFiles.Count);
        return Task.FromResult<IReadOnlyList<string>>(configFiles.Distinct().ToList());
    }

    /// <summary>
    /// Validates a single configuration file for common issues.
    /// </summary>
    private Task ValidateSingleConfigurationFileAsync(string filePath, ConfigSchema schema, ConfigValidationResult result, CancellationToken ct)
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
        if (sensitiveProperties.Any())
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
        return ValidateCommonConfigurationPatternsAsync(filePath, schema, result, ct);
    }

    /// <summary>
    /// Validates common configuration patterns and best practices.
    /// </summary>
    private Task ValidateCommonConfigurationPatternsAsync(string filePath, ConfigSchema schema, ConfigValidationResult result, CancellationToken ct)
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
        if (configFiles.Count < 2) return;

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
                    _logger.LogWarning(ex, "Failed to validate environment consistency for environment: {Environment}", env);
                }
            }
        }
    }
}