using System.Text.Json;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Configuration manager for analyzer settings and preferences
/// </summary>
public class AnalyzerConfigurationManager
{
    private readonly ILogger<AnalyzerConfigurationManager> _logger;
    private readonly string _configDirectory;
    private readonly Dictionary<string, AnalyzerConfiguration> _globalConfigurations;
    private readonly Dictionary<string, Dictionary<string, AnalyzerConfiguration>> _projectConfigurations;

    public AnalyzerConfigurationManager(ILogger<AnalyzerConfigurationManager> logger, string? basePath = null)
    {
        _logger = logger;
        _configDirectory = Path.Combine(basePath ?? Directory.GetCurrentDirectory(), ".mcpsharp", "analyzers");
        _globalConfigurations = new Dictionary<string, AnalyzerConfiguration>();
        _projectConfigurations = new Dictionary<string, Dictionary<string, AnalyzerConfiguration>>();

        Directory.CreateDirectory(_configDirectory);
        LoadConfigurations();
    }

    /// <summary>
    /// Get global configuration for an analyzer
    /// </summary>
    public AnalyzerConfiguration GetGlobalConfiguration(string analyzerId)
    {
        return _globalConfigurations.TryGetValue(analyzerId, out var config) ? config : new AnalyzerConfiguration();
    }

    /// <summary>
    /// Set global configuration for an analyzer
    /// </summary>
    public async Task SetGlobalConfigurationAsync(string analyzerId, AnalyzerConfiguration configuration)
    {
        _globalConfigurations[analyzerId] = configuration;
        await SaveGlobalConfigurationsAsync();
    }

    /// <summary>
    /// Get project-specific configuration for an analyzer
    /// </summary>
    public AnalyzerConfiguration GetProjectConfiguration(string projectPath, string analyzerId)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        if (!_projectConfigurations.TryGetValue(normalizedPath, out var projectConfigs))
        {
            return new AnalyzerConfiguration();
        }

        return projectConfigs.TryGetValue(analyzerId, out var config) ? config : new AnalyzerConfiguration();
    }

    /// <summary>
    /// Set project-specific configuration for an analyzer
    /// </summary>
    public async Task SetProjectConfigurationAsync(string projectPath, string analyzerId, AnalyzerConfiguration configuration)
    {
        var normalizedPath = Path.GetFullPath(projectPath);

        if (!_projectConfigurations.TryGetValue(normalizedPath, out var projectConfigs))
        {
            projectConfigs = new Dictionary<string, AnalyzerConfiguration>();
            _projectConfigurations[normalizedPath] = projectConfigs;
        }

        projectConfigs[analyzerId] = configuration;
        await SaveProjectConfigurationsAsync(normalizedPath);
    }

    /// <summary>
    /// Get effective configuration (merged global and project-specific)
    /// </summary>
    public AnalyzerConfiguration GetEffectiveConfiguration(string projectPath, string analyzerId)
    {
        var globalConfig = GetGlobalConfiguration(analyzerId);
        var projectConfig = GetProjectConfiguration(projectPath, analyzerId);

        return MergeConfigurations(globalConfig, projectConfig);
    }

    /// <summary>
    /// Load analyzer configuration from file
    /// </summary>
    public async Task<AnalyzerConfiguration?> LoadConfigurationFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Configuration file not found: {FilePath}", filePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var configuration = JsonSerializer.Deserialize<AnalyzerConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration from file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Save analyzer configuration to file
    /// </summary>
    public async Task<bool> SaveConfigurationToFileAsync(string filePath, AnalyzerConfiguration configuration)
    {
        try
        {
            var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration to file: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Get default configuration for an analyzer based on its rules
    /// </summary>
    public AnalyzerConfiguration GetDefaultConfiguration(IAnalyzer analyzer)
    {
        var rules = analyzer.GetRules();
        var ruleConfigs = new Dictionary<string, RuleConfiguration>();

        foreach (var rule in rules)
        {
            ruleConfigs[rule.Id] = new RuleConfiguration
            {
                IsEnabled = rule.IsEnabledByDefault,
                Severity = rule.DefaultSeverity
            };
        }

        return new AnalyzerConfiguration
        {
            IsEnabled = true,
            Rules = ruleConfigs
        };
    }

    /// <summary>
    /// Discover configuration files in a directory
    /// </summary>
    public async Task<ImmutableArray<string>> DiscoverConfigurationFilesAsync(string directoryPath)
    {
        try
        {
            var configFiles = new List<string>();
            var searchPatterns = new[]
            {
                "*.analyzer.json",
                "mcpsharp.analyzers.json",
                ".mcpsharp.analyzers.json",
                "analyzers.config.json"
            };

            foreach (var pattern in searchPatterns)
            {
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                configFiles.AddRange(files);
            }

            return configFiles.Distinct().ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering configuration files in directory: {DirectoryPath}", directoryPath);
            return ImmutableArray<string>.Empty;
        }
    }

    /// <summary>
    /// Validate analyzer configuration
    /// </summary>
    public ConfigurationValidationResult ValidateConfiguration(AnalyzerConfiguration configuration, IAnalyzer analyzer)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            var rules = analyzer.GetRules();
            var ruleIds = rules.Select(r => r.Id).ToHashSet();

            // Check for invalid rule IDs
            foreach (var ruleConfig in configuration.Rules.Keys)
            {
                if (!ruleIds.Contains(ruleConfig))
                {
                    warnings.Add($"Unknown rule ID: {ruleConfig}");
                }
            }

            // Check for missing rule configurations
            foreach (var rule in rules)
            {
                if (!configuration.Rules.ContainsKey(rule.Id) && !rule.IsEnabledByDefault)
                {
                    warnings.Add($"Rule {rule.Id} is disabled by default but no configuration provided");
                }
            }

            // Validate file patterns
            ValidateFilePatterns(configuration.IncludeFiles, "include_files", warnings);
            ValidateFilePatterns(configuration.ExcludeFiles, "exclude_files", warnings);

            // Validate custom properties
            foreach (var property in configuration.Properties)
            {
                if (string.IsNullOrEmpty(property.Key))
                {
                    errors.Add("Property with empty key found");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Validation error: {ex.Message}");
        }

        return new ConfigurationValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray()
        };
    }

    /// <summary>
    /// Export configuration to a portable format
    /// </summary>
    public async Task<string> ExportConfigurationAsync(string analyzerId, string projectPath)
    {
        try
        {
            var config = GetEffectiveConfiguration(projectPath, analyzerId);

            var exportData = new
            {
                analyzer_id = analyzerId,
                project_path = projectPath,
                exported_at = DateTime.UtcNow,
                configuration = config
            };

            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting configuration for analyzer: {AnalyzerId}", analyzerId);
            throw;
        }
    }

    /// <summary>
    /// Import configuration from a portable format
    /// </summary>
    public async Task<bool> ImportConfigurationAsync(string jsonContent)
    {
        try
        {
            var importData = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            if (!importData.TryGetProperty("analyzer_id", out var analyzerIdElement) ||
                !importData.TryGetProperty("configuration", out var configElement))
            {
                return false;
            }

            var analyzerId = analyzerIdElement.GetString() ?? string.Empty;
            var configuration = JsonSerializer.Deserialize<AnalyzerConfiguration>(configElement.GetRawText());

            if (configuration != null)
            {
                await SetGlobalConfigurationAsync(analyzerId, configuration);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing configuration");
            return false;
        }
    }

    private void LoadConfigurations()
    {
        try
        {
            // Load global configurations
            var globalConfigPath = Path.Combine(_configDirectory, "global.json");
            if (File.Exists(globalConfigPath))
            {
                var globalJson = File.ReadAllText(globalConfigPath);
                var configs = JsonSerializer.Deserialize<Dictionary<string, AnalyzerConfiguration>>(globalJson);
                if (configs != null)
                {
                    _globalConfigurations = configs;
                }
            }

            // Load project configurations
            var projectConfigPath = Path.Combine(_configDirectory, "projects");
            if (Directory.Exists(projectConfigPath))
            {
                foreach (var projectFile in Directory.GetFiles(projectConfigPath, "*.json"))
                {
                    try
                    {
                        var projectJson = File.ReadAllText(projectFile);
                        var projectPath = Path.GetFileNameWithoutExtension(projectFile);
                        var configs = JsonSerializer.Deserialize<Dictionary<string, AnalyzerConfiguration>>(projectJson);

                        if (configs != null)
                        {
                            _projectConfigurations[projectPath] = configs;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading project configuration file: {FilePath}", projectFile);
                    }
                }
            }

            _logger.LogInformation("Loaded {GlobalCount} global and {ProjectCount} project configurations",
                _globalConfigurations.Count, _projectConfigurations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzer configurations");
        }
    }

    private async Task SaveGlobalConfigurationsAsync()
    {
        try
        {
            var globalConfigPath = Path.Combine(_configDirectory, "global.json");
            var json = JsonSerializer.Serialize(_globalConfigurations, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(globalConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving global configurations");
        }
    }

    private async Task SaveProjectConfigurationsAsync(string projectPath)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_configDirectory, "projects"));

            var projectConfigPath = Path.Combine(_configDirectory, "projects", $"{Path.GetFileName(projectPath)}.json");

            if (_projectConfigurations.TryGetValue(projectPath, out var configs))
            {
                var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(projectConfigPath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving project configuration for: {ProjectPath}", projectPath);
        }
    }

    private AnalyzerConfiguration MergeConfigurations(AnalyzerConfiguration global, AnalyzerConfiguration project)
    {
        var mergedRules = new Dictionary<string, RuleConfiguration>(global.Rules);

        // Project rules override global rules
        foreach (var (ruleId, ruleConfig) in project.Rules)
        {
            mergedRules[ruleId] = ruleConfig;
        }

        var mergedProperties = new Dictionary<string, object>(global.Properties);
        foreach (var (key, value) in project.Properties)
        {
            mergedProperties[key] = value;
        }

        return new AnalyzerConfiguration
        {
            IsEnabled = project.IsEnabled,
            Properties = mergedProperties,
            Rules = mergedRules,
            IncludeFiles = project.IncludeFiles.Any() ? project.IncludeFiles : global.IncludeFiles,
            ExcludeFiles = project.ExcludeFiles.Any() ? project.ExcludeFiles : global.ExcludeFiles
        };
    }

    private void ValidateFilePatterns(ImmutableArray<string> patterns, string propertyName, List<string> warnings)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                // Try to create a regex from the pattern to validate it
                if (pattern.Contains('*') || pattern.Contains('?'))
                {
                    var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                    _ = new Regex(regexPattern);
                }
            }
            catch (Exception)
            {
                warnings.Add($"Invalid pattern in {propertyName}: {pattern}");
            }
        }
    }
}

/// <summary>
/// Result of configuration validation
/// </summary>
public record ConfigurationValidationResult
{
    public bool IsValid { get; init; }
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
}