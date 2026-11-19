using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// The open project's auto-load configuration file
/// </summary>
public sealed partial class AnalyzerAutoLoadService
{
    // Configuration files use camelCase property names (see docs/analyzer-autoload-schema.json)
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] KnownSources =
        [AnalyzerAutoLoadConfiguration.ProjectReferencesSource, AnalyzerAutoLoadConfiguration.BuiltInProvidersSource];

    // The configuration file read last, kept until the file changes
    private ConfigurationFile? _configurationFile;

    /// <summary>
    /// The path of a project's auto-load configuration: <c>.mcpsharp/autoload-config.json</c> in its root directory
    /// </summary>
    public static string GetConfigurationPath(string projectDirectory) =>
        Path.Combine(projectDirectory, ".mcpsharp", "autoload-config.json");

    /// <summary>
    /// The auto-load configuration for the open project: its <c>.mcpsharp/autoload-config.json</c>, read again
    /// whenever the file changes, or the default configuration when there is no project, no file, or a file that
    /// cannot be read
    /// </summary>
    public AnalyzerAutoLoadConfiguration GetConfiguration() => ResolveConfiguration().Configuration;

    /// <summary>
    /// The configuration for the open project, and why its configuration file was ignored, if it was
    /// </summary>
    private (AnalyzerAutoLoadConfiguration Configuration, string? Error) ResolveConfiguration()
    {
        var projectDirectory = _workspace.LoadedProjectDirectory;
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return (_defaultConfiguration, null);
        }

        var file = new FileInfo(GetConfigurationPath(projectDirectory));
        if (!file.Exists)
        {
            return (_defaultConfiguration, null);
        }

        lock (_lock)
        {
            if (_configurationFile is { } cached && cached.IsCurrent(file))
            {
                return (cached.Configuration, cached.Error);
            }
        }

        var configuration = ReadConfiguration(file.FullName, out var readError);
        string? error = null;
        if (configuration == null)
        {
            error = $"Ignored the analyzer auto-load configuration {file.FullName}, which could not be read ({readError}); using the default configuration";
            LogInvalidConfiguration(_logger, file.FullName, readError);
        }
        else
        {
            LogReadConfiguration(_logger, file.FullName);
        }

        var state = new ConfigurationFile(
            file.FullName,
            file.LastWriteTimeUtc,
            file.Length,
            configuration == null ? _defaultConfiguration : ResolveCustomPaths(configuration, projectDirectory),
            error);
        lock (_lock)
        {
            _configurationFile = state;
        }

        return (state.Configuration, state.Error);
    }

    /// <summary>
    /// Read an auto-load configuration file: null when it cannot be read or parsed, with the reason in
    /// <paramref name="error"/>
    /// </summary>
    internal static AnalyzerAutoLoadConfiguration? ReadConfiguration(string configPath, out string? error)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            var configuration = JsonSerializer.Deserialize<AnalyzerAutoLoadConfiguration>(json, ConfigurationJsonOptions);
            error = configuration == null ? "it holds no configuration object" : null;
            return configuration == null ? null : WithoutBlankEntries(configuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Load configuration from file; a missing, unreadable or malformed file gives the default configuration
    /// </summary>
    public static AnalyzerAutoLoadConfiguration LoadConfiguration(string configPath) =>
        File.Exists(configPath)
            ? ReadConfiguration(configPath, out _) ?? AnalyzerAutoLoadConfiguration.Default
            : AnalyzerAutoLoadConfiguration.Default;

    /// <summary>
    /// Report the configured analyzer sources this service doesn't know (e.g. nuget_cache, which it no longer has)
    /// </summary>
    private void ReportUnknownSources(AnalyzerAutoLoadConfiguration configuration, List<string> errors)
    {
        foreach (var source in configuration.AnalyzerSources.Where(s => !KnownSources.Contains(s, StringComparer.Ordinal)))
        {
            LogUnknownSource(_logger, source);
            errors.Add($"Ignored the unknown analyzer source '{source}'; the sources are {string.Join(" and ", KnownSources)}");
        }
    }

    private static AnalyzerAutoLoadConfiguration WithoutBlankEntries(AnalyzerAutoLoadConfiguration configuration) =>
        configuration with
        {
            AnalyzerSources = WithoutBlankEntries(configuration.AnalyzerSources),
            ExcludeAnalyzers = WithoutBlankEntries(configuration.ExcludeAnalyzers),
            CustomAnalyzerPaths = WithoutBlankEntries(configuration.CustomAnalyzerPaths)
        };

    private static ImmutableArray<string> WithoutBlankEntries(ImmutableArray<string> values) =>
        values.IsDefault ? ImmutableArray<string>.Empty : values.Where(v => !string.IsNullOrWhiteSpace(v)).ToImmutableArray();

    /// <summary>
    /// A project's configuration gives custom analyzer paths relative to the project root
    /// </summary>
    private static AnalyzerAutoLoadConfiguration ResolveCustomPaths(AnalyzerAutoLoadConfiguration configuration, string projectDirectory) =>
        configuration with
        {
            CustomAnalyzerPaths = configuration.CustomAnalyzerPaths.Select(p => Path.GetFullPath(p, projectDirectory)).ToImmutableArray()
        };

    /// <summary>
    /// A configuration file as read, current while its path, time and length are unchanged
    /// </summary>
    private sealed record ConfigurationFile(
        string Path,
        DateTime LastWriteTimeUtc,
        long Length,
        AnalyzerAutoLoadConfiguration Configuration,
        string? Error)
    {
        public bool IsCurrent(FileInfo file) =>
            string.Equals(Path, file.FullName, PathContainment.Comparison) &&
            LastWriteTimeUtc == file.LastWriteTimeUtc &&
            Length == file.Length;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Read the analyzer auto-load configuration {Path}")]
    private static partial void LogReadConfiguration(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring the analyzer auto-load configuration {Path}, which could not be read: {Error}")]
    private static partial void LogInvalidConfiguration(ILogger logger, string path, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring the unknown analyzer source '{Source}'")]
    private static partial void LogUnknownSource(ILogger logger, string source);
}
