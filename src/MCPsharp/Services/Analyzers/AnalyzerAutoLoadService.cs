using System.Collections.Immutable;
using System.Diagnostics;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for automatically loading the Roslyn analyzers that the projects of the open workspace reference
/// </summary>
/// <remarks>
/// As in an IDE, the analyzers come from the projects' analyzer references
/// (<see cref="RoslynWorkspace.GetAnalyzerReferences"/>): the analyzer assets of the NuGet packages the projects
/// restore, at the restored versions, the SDK's analyzers and any Analyzer items. Nothing else is loaded, and the
/// NuGet cache is never scanned. The open project's <c>.mcpsharp/autoload-config.json</c> configures the load
/// (see <see cref="GetConfiguration"/>). When the references or the configuration change (e.g. another project is
/// opened), the next load replaces the analyzers loaded before.
/// </remarks>
public sealed partial class AnalyzerAutoLoadService : IDisposable
{
    private readonly ILogger<AnalyzerAutoLoadService> _logger;
    private readonly RoslynAnalyzerLoader _loader;
    private readonly IAnalyzerRegistry _registry;
    private readonly RoslynWorkspace _workspace;
    private readonly AnalyzerAutoLoadConfiguration _defaultConfiguration;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _lock = new();

    // Analyzers this service registered and has not unregistered yet; only used while holding _loadLock
    private readonly List<IAnalyzer> _registeredAnalyzers = new();

    private bool _hasAutoLoaded;
    private AnalyzerAutoLoadConfiguration? _loadedConfiguration;
    private IReadOnlyList<AnalyzerReference> _loadedReferences = Array.Empty<AnalyzerReference>();
    private ImmutableArray<IAnalyzer> _autoLoadedAnalyzers = ImmutableArray<IAnalyzer>.Empty;

    /// <summary>
    /// Create the service for the projects of <paramref name="workspace"/>
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="loader">Loads the analyzers</param>
    /// <param name="registry">The registry the analyzers are registered in</param>
    /// <param name="workspace">The workspace whose projects' analyzers are loaded</param>
    /// <param name="configuration">
    /// The configuration used when the open project has no readable <c>.mcpsharp/autoload-config.json</c>
    /// (default: <see cref="AnalyzerAutoLoadConfiguration.Default"/>)
    /// </param>
    public AnalyzerAutoLoadService(
        ILogger<AnalyzerAutoLoadService> logger,
        RoslynAnalyzerLoader loader,
        IAnalyzerRegistry registry,
        RoslynWorkspace workspace,
        AnalyzerAutoLoadConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _defaultConfiguration = configuration ?? AnalyzerAutoLoadConfiguration.Default;
    }

    /// <summary>
    /// Auto-load the analyzers of the workspace's current analyzer references, as the open project's configuration
    /// says, unless they are already loaded
    /// </summary>
    public async Task<AnalyzerAutoLoadResult> EnsureAnalyzersLoadedAsync(CancellationToken cancellationToken = default)
    {
        // One load at a time, so concurrent callers don't load and register the same analyzers twice
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            var (configuration, configurationError) = ResolveConfiguration();
            var references = configuration.AutoLoadEnabled &&
                             configuration.AnalyzerSources.Contains(AnalyzerAutoLoadConfiguration.ProjectReferencesSource)
                ? _workspace.GetAnalyzerReferences()
                : Array.Empty<AnalyzerReference>();

            lock (_lock)
            {
                if (_hasAutoLoaded && ReferenceEquals(configuration, _loadedConfiguration) && references.SequenceEqual(_loadedReferences))
                {
                    return new AnalyzerAutoLoadResult
                    {
                        Success = true,
                        AlreadyLoaded = true,
                        LoadedAnalyzers = _autoLoadedAnalyzers,
                        Message = configuration.AutoLoadEnabled ? "Analyzers already auto-loaded" : "Auto-loading is disabled"
                    };
                }
            }

            return await LoadAnalyzersAsync(configuration, configurationError, references, cancellationToken);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Load analyzers from the configured sources, replacing the analyzers of the previous load
    /// </summary>
    private async Task<AnalyzerAutoLoadResult> LoadAnalyzersAsync(
        AnalyzerAutoLoadConfiguration configuration,
        string? configurationError,
        IReadOnlyList<AnalyzerReference> references,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var loadedAnalyzers = new List<IAnalyzer>();
        var errors = configurationError == null ? new List<string>() : [configurationError];

        try
        {
            // The previous load's analyzers (e.g. those of a project that is no longer open) are replaced, not kept
            await UnregisterAutoLoadedAnalyzersAsync(cancellationToken);

            if (!configuration.AutoLoadEnabled)
            {
                LogAutoLoadDisabled(_logger);
                return new AnalyzerAutoLoadResult
                {
                    Success = true,
                    LoadedAnalyzers = Publish(configuration, references, loadedAnalyzers, loaded: true),
                    LoadDuration = stopwatch.Elapsed,
                    Errors = errors.ToImmutableArray(),
                    Message = "Auto-loading is disabled"
                };
            }

            LogStartingAutoLoad(_logger, references.Count);
            ReportUnknownSources(configuration, errors);

            if (configuration.AnalyzerSources.Contains(AnalyzerAutoLoadConfiguration.ProjectReferencesSource))
            {
                var projectAnalyzers = _loader.LoadAnalyzersFromReferences(references, cancellationToken);
                loadedAnalyzers.AddRange(projectAnalyzers);
                LogLoadedFromProjectReferences(_logger, projectAnalyzers.Length, references.Count);
            }

            // Built-in providers are already registered in BuiltInCodeFixRegistry
            if (configuration.AnalyzerSources.Contains(AnalyzerAutoLoadConfiguration.BuiltInProvidersSource))
            {
                LogBuiltInProvidersAutoRegistered(_logger);
            }

            loadedAnalyzers.AddRange(await LoadFromCustomPathsAsync(configuration, errors, cancellationToken));
            loadedAnalyzers = SelectAnalyzersToRegister(configuration, loadedAnalyzers);
            await RegisterAnalyzersAsync(loadedAnalyzers, errors, cancellationToken);

            var published = Publish(configuration, references, loadedAnalyzers, loaded: true);
            LogAutoLoadCompleted(_logger, loadedAnalyzers.Count, stopwatch.Elapsed.TotalMilliseconds);

            return new AnalyzerAutoLoadResult
            {
                Success = true,
                LoadedAnalyzers = published,
                LoadDuration = stopwatch.Elapsed,
                Errors = errors.ToImmutableArray(),
                Message = $"Successfully auto-loaded {loadedAnalyzers.Count} analyzers"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFatalAutoLoadError(_logger, ex);
            return new AnalyzerAutoLoadResult
            {
                Success = false,
                LoadedAnalyzers = Publish(configuration, references, loadedAnalyzers, loaded: false),
                LoadDuration = stopwatch.Elapsed,
                Errors = errors.Append($"Fatal error: {ex.Message}").ToImmutableArray(),
                Message = $"Auto-load failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Load analyzers from the configured custom directories and assembly files
    /// </summary>
    private async Task<List<IAnalyzer>> LoadFromCustomPathsAsync(
        AnalyzerAutoLoadConfiguration configuration,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var analyzers = new List<IAnalyzer>();

        foreach (var customPath in configuration.CustomAnalyzerPaths)
        {
            try
            {
                if (Directory.Exists(customPath))
                {
                    // Every DLL in the directory is loaded to look for analyzers, so refuse one such as the NuGet cache
                    var limitError = AnalyzerDirectoryScanLimits.Check(customPath);
                    if (limitError != null)
                    {
                        LogCustomPathOverScanLimit(_logger, limitError);
                        errors.Add(limitError);
                        continue;
                    }

                    var assemblyPaths = _loader.DiscoverAnalyzerAssemblies(customPath, recursive: true);
                    var pathAnalyzers = await _loader.LoadAnalyzersFromAssembliesAsync(assemblyPaths, cancellationToken);
                    analyzers.AddRange(pathAnalyzers);
                    LogLoadedFromCustomPath(_logger, pathAnalyzers.Length, customPath);
                }
                else if (File.Exists(customPath))
                {
                    var customAnalyzers = await _loader.LoadAnalyzersFromAssemblyAsync(customPath, cancellationToken);
                    analyzers.AddRange(customAnalyzers);
                    LogLoadedFromCustomAssembly(_logger, customAnalyzers.Length, customPath);
                }
                else
                {
                    LogCustomPathNotFound(_logger, customPath);
                    errors.Add($"Custom analyzer path not found: {customPath}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogErrorLoadingFromCustomPath(_logger, ex, customPath, ex.Message);
                errors.Add($"Error loading from custom path '{customPath}': {ex.Message}");
            }
        }

        return analyzers;
    }

    /// <summary>
    /// Drop excluded analyzers, and keep one analyzer per ID: two references to the same assembly identity
    /// (e.g. copies at two paths) hold the same analyzers
    /// </summary>
    private List<IAnalyzer> SelectAnalyzersToRegister(AnalyzerAutoLoadConfiguration configuration, List<IAnalyzer> analyzers)
    {
        var selected = analyzers;
        if (configuration.ExcludeAnalyzers.Any())
        {
            selected = selected.Where(a => !IsExcluded(configuration, a)).ToList();
            LogFilteredExcluded(_logger, analyzers.Count - selected.Count);
        }

        return selected.DistinctBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// An analyzer is excluded by its ID or, for a Roslyn analyzer, by its ID without the assembly version,
    /// which keeps excluding it after its package is upgraded
    /// </summary>
    private static bool IsExcluded(AnalyzerAutoLoadConfiguration configuration, IAnalyzer analyzer) =>
        configuration.ExcludeAnalyzers.Contains(analyzer.Id) ||
        (analyzer is RoslynAnalyzerAdapter roslynAnalyzer &&
         configuration.ExcludeAnalyzers.Contains(roslynAnalyzer.VersionIndependentId));

    private async Task RegisterAnalyzersAsync(List<IAnalyzer> analyzers, List<string> errors, CancellationToken cancellationToken)
    {
        foreach (var analyzer in analyzers)
        {
            try
            {
                if (await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken))
                {
                    _registeredAnalyzers.Add(analyzer);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogErrorRegisteringAnalyzer(_logger, ex, analyzer.Id);
                errors.Add($"Failed to register analyzer '{analyzer.Id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Unregister the analyzers this service registered, and forget the previous load
    /// </summary>
    private async Task UnregisterAutoLoadedAnalyzersAsync(CancellationToken cancellationToken)
    {
        Publish(null, [], [], loaded: false);

        var unregistered = 0;
        foreach (var analyzer in _registeredAnalyzers)
        {
            // Leave the ID alone if something else has registered its own analyzer under it since
            if (ReferenceEquals(_registry.GetAnalyzer(analyzer.Id), analyzer) &&
                await _registry.UnregisterAnalyzerAsync(analyzer.Id, cancellationToken))
            {
                unregistered++;
            }
        }

        _registeredAnalyzers.Clear();
        if (unregistered > 0)
        {
            LogUnregisteredPreviousAnalyzers(_logger, unregistered);
        }
    }

    /// <summary>
    /// Record the outcome of a load, returning the loaded analyzers as published
    /// </summary>
    private ImmutableArray<IAnalyzer> Publish(
        AnalyzerAutoLoadConfiguration? configuration,
        IReadOnlyList<AnalyzerReference> references,
        List<IAnalyzer> analyzers,
        bool loaded)
    {
        var published = analyzers.ToImmutableArray();
        lock (_lock)
        {
            _hasAutoLoaded = loaded;
            _loadedConfiguration = configuration;
            _loadedReferences = references;
            _autoLoadedAnalyzers = published;
        }

        return published;
    }

    /// <summary>
    /// The projects of the workspace that compile a file, and their analyzer references: the project analyzers
    /// that apply to the file
    /// </summary>
    public FileAnalyzerScope GetFileAnalyzerScope(string filePath) => _workspace.GetFileAnalyzerScope(filePath);

    /// <summary>
    /// Make the next <see cref="EnsureAnalyzersLoadedAsync"/> load again, replacing the analyzers loaded so far
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _hasAutoLoaded = false;
        }
    }

    /// <summary>
    /// Get auto-load status: the configuration of the last load (the default configuration before the first one)
    /// </summary>
    public AnalyzerAutoLoadStatus GetStatus()
    {
        lock (_lock)
        {
            return new AnalyzerAutoLoadStatus
            {
                HasAutoLoaded = _hasAutoLoaded,
                AutoLoadedCount = _autoLoadedAnalyzers.Length,
                Configuration = _loadedConfiguration ?? _defaultConfiguration
            };
        }
    }

    public void Dispose() => _loadLock.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-loading is disabled in configuration")]
    private static partial void LogAutoLoadDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting auto-load of Roslyn analyzers from {ReferenceCount} project analyzer references...")]
    private static partial void LogStartingAutoLoad(ILogger logger, int referenceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from {ReferenceCount} project analyzer references")]
    private static partial void LogLoadedFromProjectReferences(ILogger logger, int count, int referenceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unregistered {Count} analyzers of the previous auto-load")]
    private static partial void LogUnregisteredPreviousAnalyzers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Built-in code fix providers are automatically registered via BuiltInCodeFixRegistry")]
    private static partial void LogBuiltInProvidersAutoRegistered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from custom path: {Path}")]
    private static partial void LogLoadedFromCustomPath(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from custom assembly: {Path}")]
    private static partial void LogLoadedFromCustomAssembly(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping custom analyzer path: {Reason}")]
    private static partial void LogCustomPathOverScanLimit(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom analyzer path not found: {Path}")]
    private static partial void LogCustomPathNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error loading from custom path '{Path}': {ErrorMessage}")]
    private static partial void LogErrorLoadingFromCustomPath(ILogger logger, Exception exception, string path, string errorMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filtered out {Count} excluded analyzers")]
    private static partial void LogFilteredExcluded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error registering analyzer: {AnalyzerId}")]
    private static partial void LogErrorRegisteringAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-load completed: {Count} analyzers loaded in {Duration}ms")]
    private static partial void LogAutoLoadCompleted(ILogger logger, int count, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error during analyzer auto-load")]
    private static partial void LogFatalAutoLoadError(ILogger logger, Exception exception);
}

/// <summary>
/// Configuration for analyzer auto-loading, read from a project's <c>.mcpsharp/autoload-config.json</c>
/// </summary>
public record AnalyzerAutoLoadConfiguration
{
    /// <summary>
    /// Analyzer source: the analyzer references of the workspace's projects
    /// </summary>
    public const string ProjectReferencesSource = "project_references";

    /// <summary>
    /// Analyzer source: the built-in code fix providers (registered by BuiltInCodeFixRegistry)
    /// </summary>
    public const string BuiltInProvidersSource = "builtin_providers";

    public bool AutoLoadEnabled { get; init; } = true;

    /// <summary>
    /// Start loading the analyzers as soon as MCPsharp has loaded the project at startup, instead of on first use
    /// </summary>
    public bool LoadOnStartup { get; init; }

    public ImmutableArray<string> AnalyzerSources { get; init; } = ImmutableArray.Create(ProjectReferencesSource, BuiltInProvidersSource);
    public ImmutableArray<string> ExcludeAnalyzers { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> CustomAnalyzerPaths { get; init; } = ImmutableArray<string>.Empty;

    public static readonly AnalyzerAutoLoadConfiguration Default = new()
    {
        AutoLoadEnabled = true,
        LoadOnStartup = false,
        AnalyzerSources = ImmutableArray.Create(ProjectReferencesSource, BuiltInProvidersSource),
        ExcludeAnalyzers = ImmutableArray<string>.Empty,
        CustomAnalyzerPaths = ImmutableArray<string>.Empty
    };
}

/// <summary>
/// Result of auto-loading analyzers
/// </summary>
public record AnalyzerAutoLoadResult
{
    public required bool Success { get; init; }
    public bool AlreadyLoaded { get; init; }
    public ImmutableArray<IAnalyzer> LoadedAnalyzers { get; init; } = ImmutableArray<IAnalyzer>.Empty;
    public TimeSpan LoadDuration { get; init; }
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
    public string? Message { get; init; }
}

/// <summary>
/// Current auto-load status
/// </summary>
public record AnalyzerAutoLoadStatus
{
    public bool HasAutoLoaded { get; init; }
    public int AutoLoadedCount { get; init; }
    public AnalyzerAutoLoadConfiguration Configuration { get; init; } = AnalyzerAutoLoadConfiguration.Default;
}
