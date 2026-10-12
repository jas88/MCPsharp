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

public partial class AnalyzerConfigurationService : IAnalyzerConfigurationService
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

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
            LogLoadingConfiguration(_logger, analyzerId);

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
                LogNoConfigurationFile(_logger, analyzerId, configPath);
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
                LogUnknownConfigurationFormat(_logger, configPath);
                return null;
            }

            if (configuration != null)
            {
                // Cache the configuration
                lock (_lock)
                {
                    _configCache[cacheKey] = configuration;
                }

                LogLoadedConfiguration(_logger, analyzerId);
            }

            return configuration;
        }
        catch (Exception ex)
        {
            LogErrorLoadingConfiguration(_logger, ex, analyzerId);
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
            LogSavingConfiguration(_logger, analyzerId);

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
                configContent = JsonSerializer.Serialize(configuration, IndentedJsonOptions);
            }

            await File.WriteAllTextAsync(configPath, configContent, cancellationToken);

            // Update cache
            var cacheKey = GetCacheKey(analyzerId, projectPath);
            lock (_lock)
            {
                _configCache[cacheKey] = configuration;
            }

            LogSavedConfiguration(_logger, analyzerId);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorSavingConfiguration(_logger, ex, analyzerId);
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
            LogUpdatingRuleConfiguration(_logger, analyzerId, ruleId);

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
            LogErrorUpdatingRuleConfiguration(_logger, ex, analyzerId, ruleId);
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
            LogErrorGettingRuleConfiguration(_logger, ex, analyzerId, ruleId);
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
            LogSettingEnabledState(_logger, analyzerId, enabled);

            // Load existing configuration or create new one
            var configuration = await LoadConfigurationAsync(analyzerId, projectPath, cancellationToken)
                ?? new AnalyzerConfiguration();

            var updatedConfiguration = configuration with { IsEnabled = enabled };

            return await SaveConfigurationAsync(analyzerId, updatedConfiguration, projectPath, cancellationToken);
        }
        catch (Exception ex)
        {
            LogErrorSettingEnabledState(_logger, ex, analyzerId);
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
                    LogErrorLoadingConfigurationFile(_logger, ex, configFile);
                }
            }

            return configurations.ToImmutableDictionary();
        }
        catch (Exception ex)
        {
            LogErrorGettingAllConfigurations(_logger, ex);
            return ImmutableDictionary<string, AnalyzerConfiguration>.Empty;
        }
    }

    public Task<bool> DeleteConfigurationAsync(
        string analyzerId,
        string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogDeletingConfiguration(_logger, analyzerId);

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

            LogDeletedConfiguration(_logger, analyzerId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogErrorDeletingConfiguration(_logger, ex, analyzerId);
            return Task.FromResult(false);
        }
    }

    private static string GetConfigurationPath(string analyzerId, string? projectPath)
    {
        var configDirectory = GetConfigurationDirectory(projectPath);
        return Path.Combine(configDirectory, $"analyzer_{analyzerId}.json");
    }

    private static string GetConfigurationDirectory(string? projectPath)
    {
        if (!string.IsNullOrEmpty(projectPath))
        {
            return Path.Combine(projectPath, ".mcpsharp", "analyzers");
        }

        // Use user's home directory
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, ".mcpsharp", "analyzers");
    }

    private static string GetCacheKey(string analyzerId, string? projectPath)
    {
        return string.IsNullOrEmpty(projectPath)
            ? analyzerId
            : $"{projectPath}::{analyzerId}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading configuration for analyzer: {AnalyzerId}")]
    private static partial void LogLoadingConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No configuration file found for analyzer {AnalyzerId} at {ConfigPath}")]
    private static partial void LogNoConfigurationFile(ILogger logger, string analyzerId, string configPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown configuration file format: {ConfigPath}")]
    private static partial void LogUnknownConfigurationFormat(ILogger logger, string configPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded configuration for analyzer: {AnalyzerId}")]
    private static partial void LogLoadedConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading configuration for analyzer: {AnalyzerId}")]
    private static partial void LogErrorLoadingConfiguration(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saving configuration for analyzer: {AnalyzerId}")]
    private static partial void LogSavingConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved configuration for analyzer: {AnalyzerId}")]
    private static partial void LogSavedConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error saving configuration for analyzer: {AnalyzerId}")]
    private static partial void LogErrorSavingConfiguration(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating rule configuration for analyzer {AnalyzerId}, rule {RuleId}")]
    private static partial void LogUpdatingRuleConfiguration(ILogger logger, string analyzerId, string ruleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error updating rule configuration for analyzer {AnalyzerId}, rule {RuleId}")]
    private static partial void LogErrorUpdatingRuleConfiguration(ILogger logger, Exception exception, string analyzerId, string ruleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting rule configuration for analyzer {AnalyzerId}, rule {RuleId}")]
    private static partial void LogErrorGettingRuleConfiguration(ILogger logger, Exception exception, string analyzerId, string ruleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting analyzer {AnalyzerId} enabled state to: {Enabled}")]
    private static partial void LogSettingEnabledState(ILogger logger, string analyzerId, bool enabled);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error setting analyzer enabled state for: {AnalyzerId}")]
    private static partial void LogErrorSettingEnabledState(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error loading configuration file: {ConfigFile}")]
    private static partial void LogErrorLoadingConfigurationFile(ILogger logger, Exception exception, string configFile);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting all configurations")]
    private static partial void LogErrorGettingAllConfigurations(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting configuration for analyzer: {AnalyzerId}")]
    private static partial void LogDeletingConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted configuration for analyzer: {AnalyzerId}")]
    private static partial void LogDeletedConfiguration(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error deleting configuration for analyzer: {AnalyzerId}")]
    private static partial void LogErrorDeletingConfiguration(ILogger logger, Exception exception, string analyzerId);
}
