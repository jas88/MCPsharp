using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for managing analyzer configurations
/// </summary>
public interface IAnalyzerConfigurationService
{
    Task<AnalyzerConfiguration?> LoadConfigurationAsync(string analyzerId, string? projectPath = null, CancellationToken cancellationToken = default);
    Task<bool> SaveConfigurationAsync(string analyzerId, AnalyzerConfiguration configuration, string? projectPath = null, CancellationToken cancellationToken = default);
    Task<bool> UpdateRuleConfigurationAsync(string analyzerId, string ruleId, RuleConfiguration ruleConfig, string? projectPath = null, CancellationToken cancellationToken = default);
    Task<RuleConfiguration?> GetRuleConfigurationAsync(string analyzerId, string ruleId, string? projectPath = null, CancellationToken cancellationToken = default);
    Task<bool> SetAnalyzerEnabledAsync(string analyzerId, bool enabled, string? projectPath = null, CancellationToken cancellationToken = default);
    Task<ImmutableDictionary<string, AnalyzerConfiguration>> GetAllConfigurationsAsync(string? projectPath = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteConfigurationAsync(string analyzerId, string? projectPath = null, CancellationToken cancellationToken = default);
}

public class AnalyzerConfigurationService : IAnalyzerConfigurationService
{
    private readonly ILogger<AnalyzerConfigurationService> _logger;
    private readonly Dictionary<string, AnalyzerConfiguration> _configCache;
    private readonly object _lock = new();
    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public AnalyzerConfigurationService(ILogger<AnalyzerConfigurationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configCache = new Dictionary<string, AnalyzerConfiguration>();

        // Initialize YAML serializer/deserializer
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public async Task<AnalyzerConfiguration?> LoadConfigurationAsync(
        string analyzerId,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading configuration for analyzer: {AnalyzerId}", analyzerId);

            // Check cache first
            var cacheKey = GetCacheKey(analyzerId, projectPath);
            lock (_lock)
            {
                if (_configCache.TryGetValue(cacheKey, out var cachedConfig))
                {
                    return cachedConfig;
                }
            }

            // Try to load from file
            var configPath = GetConfigurationPath(analyzerId, projectPath);
            if (!File.Exists(configPath))
            {
                _logger.LogInformation("No configuration file found for analyzer {AnalyzerId} at {ConfigPath}",
                    analyzerId, configPath);
                return null;
            }

            var configContent = await File.ReadAllTextAsync(configPath, cancellationToken);
            AnalyzerConfiguration? configuration;

            // Determine format by extension
            if (configPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                configuration = JsonSerializer.Deserialize<AnalyzerConfiguration>(configContent);
            }
            else if (configPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                     configPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                configuration = _yamlDeserializer.Deserialize<AnalyzerConfiguration>(configContent);
            }
            else
            {
                _logger.LogWarning("Unknown configuration file format: {ConfigPath}", configPath);
                return null;
            }

            if (configuration != null)
            {
                // Cache the configuration
                lock (_lock)
                {
                    _configCache[cacheKey] = configuration;
                }

                _logger.LogInformation("Loaded configuration for analyzer: {AnalyzerId}", analyzerId);
            }

            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration for analyzer: {AnalyzerId}", analyzerId);
            return null;
        }
    }

    public async Task<bool> SaveConfigurationAsync(
        string analyzerId,
        AnalyzerConfiguration configuration,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Saving configuration for analyzer: {AnalyzerId}", analyzerId);

            var configPath = GetConfigurationPath(analyzerId, projectPath);
            var configDirectory = Path.GetDirectoryName(configPath);

            if (!string.IsNullOrEmpty(configDirectory) && !Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            string configContent;

            // Save as JSON by default
            if (configPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                configPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                configContent = _yamlSerializer.Serialize(configuration);
            }
            else
            {
                configContent = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            await File.WriteAllTextAsync(configPath, configContent, cancellationToken);

            // Update cache
            var cacheKey = GetCacheKey(analyzerId, projectPath);
            lock (_lock)
            {
                _configCache[cacheKey] = configuration;
            }

            _logger.LogInformation("Saved configuration for analyzer: {AnalyzerId}", analyzerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration for analyzer: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    public async Task<bool> UpdateRuleConfigurationAsync(
        string analyzerId,
        string ruleId,
        RuleConfiguration ruleConfig,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating rule configuration for analyzer {AnalyzerId}, rule {RuleId}",
                analyzerId, ruleId);

            // Load existing configuration or create new one
            var configuration = await LoadConfigurationAsync(analyzerId, projectPath, cancellationToken)
                ?? new AnalyzerConfiguration();

            // Update rule configuration
            var updatedRules = new Dictionary<string, RuleConfiguration>(configuration.Rules)
            {
                [ruleId] = ruleConfig
            };

            var updatedConfiguration = configuration with { Rules = updatedRules };

            // Save updated configuration
            return await SaveConfigurationAsync(analyzerId, updatedConfiguration, projectPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating rule configuration for analyzer {AnalyzerId}, rule {RuleId}",
                analyzerId, ruleId);
            return false;
        }
    }

    public async Task<RuleConfiguration?> GetRuleConfigurationAsync(
        string analyzerId,
        string ruleId,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = await LoadConfigurationAsync(analyzerId, projectPath, cancellationToken);
            if (configuration?.Rules.TryGetValue(ruleId, out var ruleConfig) == true)
            {
                return ruleConfig;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rule configuration for analyzer {AnalyzerId}, rule {RuleId}",
                analyzerId, ruleId);
            return null;
        }
    }

    public async Task<bool> SetAnalyzerEnabledAsync(
        string analyzerId,
        bool enabled,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Setting analyzer {AnalyzerId} enabled state to: {Enabled}",
                analyzerId, enabled);

            // Load existing configuration or create new one
            var configuration = await LoadConfigurationAsync(analyzerId, projectPath, cancellationToken)
                ?? new AnalyzerConfiguration();

            var updatedConfiguration = configuration with { IsEnabled = enabled };

            return await SaveConfigurationAsync(analyzerId, updatedConfiguration, projectPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting analyzer enabled state for: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    public async Task<ImmutableDictionary<string, AnalyzerConfiguration>> GetAllConfigurationsAsync(
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configurations = new Dictionary<string, AnalyzerConfiguration>();
            var configDirectory = GetConfigurationDirectory(projectPath);

            if (!Directory.Exists(configDirectory))
            {
                return ImmutableDictionary<string, AnalyzerConfiguration>.Empty;
            }

            // Find all configuration files
            var configFiles = Directory.GetFiles(configDirectory, "*.json")
                .Concat(Directory.GetFiles(configDirectory, "*.yaml"))
                .Concat(Directory.GetFiles(configDirectory, "*.yml"));

            foreach (var configFile in configFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(configFile);
                    var analyzerId = fileName.Replace("analyzer_", "");

                    var config = await LoadConfigurationAsync(analyzerId, projectPath, cancellationToken);
                    if (config != null)
                    {
                        configurations[analyzerId] = config;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading configuration file: {ConfigFile}", configFile);
                }
            }

            return configurations.ToImmutableDictionary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all configurations");
            return ImmutableDictionary<string, AnalyzerConfiguration>.Empty;
        }
    }

    public async Task<bool> DeleteConfigurationAsync(
        string analyzerId,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting configuration for analyzer: {AnalyzerId}", analyzerId);

            var configPath = GetConfigurationPath(analyzerId, projectPath);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            // Remove from cache
            var cacheKey = GetCacheKey(analyzerId, projectPath);
            lock (_lock)
            {
                _configCache.Remove(cacheKey);
            }

            _logger.LogInformation("Deleted configuration for analyzer: {AnalyzerId}", analyzerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting configuration for analyzer: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    private string GetConfigurationPath(string analyzerId, string? projectPath)
    {
        var configDirectory = GetConfigurationDirectory(projectPath);
        return Path.Combine(configDirectory, $"analyzer_{analyzerId}.json");
    }

    private string GetConfigurationDirectory(string? projectPath)
    {
        if (!string.IsNullOrEmpty(projectPath))
        {
            return Path.Combine(projectPath, ".mcpsharp", "analyzers");
        }

        // Use user's home directory
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, ".mcpsharp", "analyzers");
    }

    private string GetCacheKey(string analyzerId, string? projectPath)
    {
        return string.IsNullOrEmpty(projectPath)
            ? analyzerId
            : $"{projectPath}::{analyzerId}";
    }
}
