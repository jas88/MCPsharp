using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for managing analyzer configurations
/// </summary>
public class AnalyzerConfigurationManager
{
    private readonly ILogger<AnalyzerConfigurationManager> _logger;
    private readonly string _configDirectory;
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
        _configDirectory = Path.Combine(baseDirectory, ".mcpsharp");
        _configFilePath = Path.Combine(_configDirectory, "analyzer-config.json");

        // Ensure directory exists
        Directory.CreateDirectory(_configDirectory);

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
            _logger.LogInformation("Updating analyzer configuration");

            _currentConfiguration = configuration;

            await SaveConfigurationToFileAsync(configuration, cancellationToken);

            _logger.LogInformation("Analyzer configuration updated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating analyzer configuration");
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
            _logger.LogInformation("Setting analyzer {AnalyzerId} enabled={Enabled}", analyzerId, enabled);

            if (!_currentConfiguration.Analyzers.ContainsKey(analyzerId))
            {
                _currentConfiguration.Analyzers[analyzerId] = new AnalyzerConfigurationEntry
                {
                    Enabled = enabled
                };
            }
            else
            {
                _currentConfiguration.Analyzers[analyzerId] = _currentConfiguration.Analyzers[analyzerId] with
                {
                    Enabled = enabled
                };
            }

            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting analyzer {AnalyzerId} enabled state", analyzerId);
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
            _logger.LogInformation("Setting rule {RuleId} severity to {Severity} for analyzer {AnalyzerId}",
                ruleId, severity, analyzerId);

            if (!_currentConfiguration.Analyzers.ContainsKey(analyzerId))
            {
                _currentConfiguration.Analyzers[analyzerId] = new AnalyzerConfigurationEntry
                {
                    Enabled = true,
                    RuleSeverityOverrides = new Dictionary<string, IssueSeverity> { [ruleId] = severity }
                };
            }
            else
            {
                var entry = _currentConfiguration.Analyzers[analyzerId];
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
            _logger.LogError(ex, "Error setting rule {RuleId} severity", ruleId);
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
            _logger.LogInformation("Resetting analyzer configuration to defaults");

            _currentConfiguration = CreateDefaultConfiguration();
            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            _logger.LogInformation("Analyzer configuration reset to defaults");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting analyzer configuration");
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
            _logger.LogInformation("Updating global analyzer settings");

            _currentConfiguration = _currentConfiguration with
            {
                Global = settings
            };

            await SaveConfigurationToFileAsync(_currentConfiguration, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating global analyzer settings");
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
            _logger.LogInformation("Exporting analyzer configuration to {FilePath}", filePath);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_currentConfiguration, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            _logger.LogInformation("Configuration exported successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting configuration to {FilePath}", filePath);
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
            _logger.LogInformation("Importing analyzer configuration from {FilePath}", filePath);

            if (!File.Exists(filePath))
            {
                _logger.LogError("Configuration file not found: {FilePath}", filePath);
                return false;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var configuration = JsonSerializer.Deserialize<AnalyzerConfigurationFile>(json, _jsonOptions);

            if (configuration == null)
            {
                _logger.LogError("Failed to deserialize configuration from {FilePath}", filePath);
                return false;
            }

            _currentConfiguration = configuration;
            await SaveConfigurationToFileAsync(configuration, cancellationToken);

            _logger.LogInformation("Configuration imported successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing configuration from {FilePath}", filePath);
            return false;
        }
    }

    private AnalyzerConfigurationFile? LoadConfigurationFromFile()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogInformation("No existing configuration file found at {ConfigFilePath}", _configFilePath);
                return null;
            }

            var json = File.ReadAllText(_configFilePath);
            var configuration = JsonSerializer.Deserialize<AnalyzerConfigurationFile>(json, _jsonOptions);

            _logger.LogInformation("Loaded analyzer configuration from {ConfigFilePath}", _configFilePath);
            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration from {ConfigFilePath}", _configFilePath);
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

            _logger.LogDebug("Saved analyzer configuration to {ConfigFilePath}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration to {ConfigFilePath}", _configFilePath);
            throw;
        }
    }

    private AnalyzerConfigurationFile CreateDefaultConfiguration()
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
