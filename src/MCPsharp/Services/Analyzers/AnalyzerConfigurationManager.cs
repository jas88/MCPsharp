using System.Text.Json;
using System.Text.Json.Serialization;
using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for managing analyzer configurations
/// </summary>
public partial class AnalyzerConfigurationManager
{
    private readonly ILogger<AnalyzerConfigurationManager> _logger;
    private readonly string _configFilePath;
    private AnalyzerConfigurationFile _currentConfiguration;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public AnalyzerConfigurationManager(
        ILogger<AnalyzerConfigurationManager>? logger = null,
        string? basePath = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalyzerConfigurationManager>.Instance;

        // Use .mcpsharp directory in the specified path or current directory
        var baseDirectory = basePath ?? Directory.GetCurrentDirectory();
        var configDirectory = Path.Combine(baseDirectory, ".mcpsharp");
        _configFilePath = Path.Combine(configDirectory, "analyzer-config.json");

        // Ensure directory exists
        Directory.CreateDirectory(configDirectory);

        // Load or create default configuration
        _currentConfiguration = LoadConfigurationFromFile() ?? CreateDefaultConfiguration();
    }

    /// <summary>
    /// Get the current analyzer configuration
    /// </summary>
    public AnalyzerConfigurationFile GetConfiguration()
    {
        return _currentConfiguration;
    }

    /// <summary>
    /// Update analyzer configuration
    /// </summary>
    public async Task<bool> UpdateConfigurationAsync(
        AnalyzerConfigurationFile configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogUpdatingConfiguration(_logger);

            _currentConfiguration = configuration;

            await SaveConfigurationToFileAsync(configuration, cancellationToken);

            LogConfigurationUpdated(_logger);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorUpdatingConfiguration(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Enable or disable an analyzer by ID
    /// </summary>
    public async Task<bool> SetAnalyzerEnabledAsync(
        string analyzerId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogSettingAnalyzerEnabled(_logger, analyzerId, enabled);

            if (_currentConfiguration.Analyzers.TryGetValue(analyzerId, out var existingEntry))
            {
                _currentConfiguration.Analyzers[analyzerId] = existingEntry with
                {
                    Enabled = enabled
                };
            }
            else
            {
                _currentConfiguration.Analyzers[analyzerId] = new AnalyzerConfigurationEntry
                {
                    Enabled = enabled
                };
            }

            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            LogErrorSettingAnalyzerEnabled(_logger, ex, analyzerId);
            return false;
        }
    }

    /// <summary>
    /// Set severity override for a specific rule
    /// </summary>
    public async Task<bool> SetRuleSeverityAsync(
        string analyzerId,
        string ruleId,
        IssueSeverity severity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogSettingRuleSeverity(_logger, ruleId, severity, analyzerId);

            if (!_currentConfiguration.Analyzers.TryGetValue(analyzerId, out var entry))
            {
                _currentConfiguration.Analyzers[analyzerId] = new AnalyzerConfigurationEntry
                {
                    Enabled = true,
                    RuleSeverityOverrides = new Dictionary<string, IssueSeverity> { [ruleId] = severity }
                };
            }
            else
            {
                var overrides = new Dictionary<string, IssueSeverity>(entry.RuleSeverityOverrides)
                {
                    [ruleId] = severity
                };

                _currentConfiguration.Analyzers[analyzerId] = entry with
                {
                    RuleSeverityOverrides = overrides
                };
            }

            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            LogErrorSettingRuleSeverity(_logger, ex, ruleId);
            return false;
        }
    }

    /// <summary>
    /// Reset configuration to defaults
    /// </summary>
    public async Task<bool> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LogResettingToDefaults(_logger);

            _currentConfiguration = CreateDefaultConfiguration();
            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            LogResetToDefaults(_logger);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorResettingConfiguration(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Get configuration for a specific analyzer
    /// </summary>
    public AnalyzerConfigurationEntry? GetAnalyzerConfiguration(string analyzerId)
    {
        return _currentConfiguration.Analyzers.TryGetValue(analyzerId, out var config)
            ? config
            : null;
    }

    /// <summary>
    /// Update global configuration settings
    /// </summary>
    public async Task<bool> UpdateGlobalSettingsAsync(
        GlobalAnalyzerSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogUpdatingGlobalSettings(_logger);

            _currentConfiguration = _currentConfiguration with
            {
                Global = settings
            };

            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            LogErrorUpdatingGlobalSettings(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Export configuration to a file
    /// </summary>
    public async Task<bool> ExportConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogExportingConfiguration(_logger, filePath);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_currentConfiguration, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            LogConfigurationExported(_logger);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorExportingConfiguration(_logger, ex, filePath);
            return false;
        }
    }

    /// <summary>
    /// Import configuration from a file
    /// </summary>
    public async Task<bool> ImportConfigurationAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogImportingConfiguration(_logger, filePath);

            if (!File.Exists(filePath))
            {
                LogConfigurationFileNotFound(_logger, filePath);
                return false;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var configuration = JsonSerializer.Deserialize<AnalyzerConfigurationFile>(json, _jsonOptions);

            if (configuration == null)
            {
                LogDeserializationFailed(_logger, filePath);
                return false;
            }

            _currentConfiguration = configuration;
            await SaveConfigurationToFileAsync(configuration, cancellationToken);

            LogConfigurationImported(_logger);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorImportingConfiguration(_logger, ex, filePath);
            return false;
        }
    }

    private AnalyzerConfigurationFile? LoadConfigurationFromFile()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                LogNoExistingConfigurationFile(_logger, _configFilePath);
                return null;
            }

            var json = File.ReadAllText(_configFilePath);
            var configuration = JsonSerializer.Deserialize<AnalyzerConfigurationFile>(json, _jsonOptions);

            LogLoadedConfiguration(_logger, _configFilePath);
            return configuration;
        }
        catch (Exception ex)
        {
            LogErrorLoadingConfiguration(_logger, ex, _configFilePath);
            return null;
        }
    }

    private async Task SaveConfigurationToFileAsync(
        AnalyzerConfigurationFile configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(configuration, _jsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);

            LogSavedConfiguration(_logger, _configFilePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Callers log the failure; add the file path as context rather than logging twice.
            throw new IOException($"Error saving configuration to {_configFilePath}", ex);
        }
    }

    private static AnalyzerConfigurationFile CreateDefaultConfiguration()
    {
        return new AnalyzerConfigurationFile
        {
            Version = "1.0",
            Analyzers = new Dictionary<string, AnalyzerConfigurationEntry>(),
            Global = new GlobalAnalyzerSettings
            {
                ParallelExecution = true,
                MaxFileSize = 10 * 1024 * 1024, // 10MB
                DefaultSeverity = IssueSeverity.Warning,
                EnabledByDefault = true
            }
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating analyzer configuration")]
    private static partial void LogUpdatingConfiguration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer configuration updated successfully")]
    private static partial void LogConfigurationUpdated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error updating analyzer configuration")]
    private static partial void LogErrorUpdatingConfiguration(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting analyzer {AnalyzerId} enabled={Enabled}")]
    private static partial void LogSettingAnalyzerEnabled(ILogger logger, string analyzerId, bool enabled);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error setting analyzer {AnalyzerId} enabled state")]
    private static partial void LogErrorSettingAnalyzerEnabled(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting rule {RuleId} severity to {Severity} for analyzer {AnalyzerId}")]
    private static partial void LogSettingRuleSeverity(ILogger logger, string ruleId, IssueSeverity severity, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error setting rule {RuleId} severity")]
    private static partial void LogErrorSettingRuleSeverity(ILogger logger, Exception exception, string ruleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resetting analyzer configuration to defaults")]
    private static partial void LogResettingToDefaults(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer configuration reset to defaults")]
    private static partial void LogResetToDefaults(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resetting analyzer configuration")]
    private static partial void LogErrorResettingConfiguration(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating global analyzer settings")]
    private static partial void LogUpdatingGlobalSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error updating global analyzer settings")]
    private static partial void LogErrorUpdatingGlobalSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exporting analyzer configuration to {FilePath}")]
    private static partial void LogExportingConfiguration(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration exported successfully")]
    private static partial void LogConfigurationExported(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error exporting configuration to {FilePath}")]
    private static partial void LogErrorExportingConfiguration(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Importing analyzer configuration from {FilePath}")]
    private static partial void LogImportingConfiguration(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Configuration file not found: {FilePath}")]
    private static partial void LogConfigurationFileNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize configuration from {FilePath}")]
    private static partial void LogDeserializationFailed(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration imported successfully")]
    private static partial void LogConfigurationImported(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error importing configuration from {FilePath}")]
    private static partial void LogErrorImportingConfiguration(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "No existing configuration file found at {ConfigFilePath}")]
    private static partial void LogNoExistingConfigurationFile(ILogger logger, string configFilePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded analyzer configuration from {ConfigFilePath}")]
    private static partial void LogLoadedConfiguration(ILogger logger, string configFilePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading configuration from {ConfigFilePath}")]
    private static partial void LogErrorLoadingConfiguration(ILogger logger, Exception exception, string configFilePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved analyzer configuration to {ConfigFilePath}")]
    private static partial void LogSavedConfiguration(ILogger logger, string configFilePath);
}

/// <summary>
/// Analyzer configuration file structure
/// </summary>
public record AnalyzerConfigurationFile
{
    public string Version { get; init; } = "1.0";
    public Dictionary<string, AnalyzerConfigurationEntry> Analyzers { get; init; } = new();
    public GlobalAnalyzerSettings Global { get; init; } = new();
}

/// <summary>
/// Configuration entry for a single analyzer
/// </summary>
public record AnalyzerConfigurationEntry
{
    public bool Enabled { get; init; } = true;
    public Dictionary<string, IssueSeverity> RuleSeverityOverrides { get; init; } = new();
    public Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// Global analyzer settings
/// </summary>
public record GlobalAnalyzerSettings
{
    public bool ParallelExecution { get; init; } = true;
    public long MaxFileSize { get; init; } = 10 * 1024 * 1024;
    public IssueSeverity DefaultSeverity { get; init; } = IssueSeverity.Warning;
    public bool EnabledByDefault { get; init; } = true;
}
