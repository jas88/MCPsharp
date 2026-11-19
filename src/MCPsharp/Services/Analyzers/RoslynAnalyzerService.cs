using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// High-level service for managing and running Roslyn analyzers
/// </summary>
public interface IRoslynAnalyzerService
{
    Task<ImmutableArray<IAnalyzer>> LoadAnalyzersAsync(string assemblyPath, CancellationToken cancellationToken = default);
    Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromDirectoryAsync(string directory, CancellationToken cancellationToken = default);
    Task<AnalyzerRunResult> RunAnalyzersAsync(string targetPath, IEnumerable<string>? analyzerIds = null, CancellationToken cancellationToken = default);
    ImmutableArray<IAnalyzer> GetLoadedAnalyzers();
    IAnalyzer? GetAnalyzer(string analyzerId);

    /// <summary>
    /// Run the configured analyzer auto-load (if any) so <see cref="GetLoadedAnalyzers"/> reflects it
    /// without having to run the analyzers first.
    /// </summary>
    Task EnsureAnalyzersLoadedAsync(CancellationToken cancellationToken = default);
}

public partial class RoslynAnalyzerService : IRoslynAnalyzerService
{
    private readonly RoslynAnalyzerLoader _loader;
    private readonly IAnalyzerHost _analyzerHost;
    private readonly ILogger<RoslynAnalyzerService> _logger;
    private readonly AnalyzerAutoLoadService? _autoLoadService;
    private readonly IAnalyzerRegistry? _registry;
    private readonly Dictionary<string, IAnalyzer> _loadedAnalyzers = new();
    private readonly object _lock = new();
    private ImmutableArray<IAnalyzer> _autoLoadedAnalyzers = ImmutableArray<IAnalyzer>.Empty;

    /// <summary>
    /// Create a service that runs Roslyn analyzers through <paramref name="analyzerHost"/>.
    /// </summary>
    /// <param name="loader">Loads the analyzers of explicitly given assemblies and directories.</param>
    /// <param name="analyzerHost">Runs the analyzers, taking them from its analyzer registry.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="autoLoadService">Auto-loads the analyzers the open project references, if given.</param>
    /// <param name="registry">
    /// The analyzer host's registry. Explicitly loaded analyzers are registered there so the host can run them;
    /// without it they are listed but not run.
    /// </param>
    public RoslynAnalyzerService(
        RoslynAnalyzerLoader loader,
        IAnalyzerHost analyzerHost,
        ILogger<RoslynAnalyzerService> logger,
        AnalyzerAutoLoadService? autoLoadService = null,
        IAnalyzerRegistry? registry = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _analyzerHost = analyzerHost ?? throw new ArgumentNullException(nameof(analyzerHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _autoLoadService = autoLoadService;
        _registry = registry;
    }

    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogLoadingAnalyzers(_logger, assemblyPath);

            var analyzers = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);

            // Register loaded analyzers
            lock (_lock)
            {
                foreach (var analyzer in analyzers)
                {
                    _loadedAnalyzers[analyzer.Id] = analyzer;
                }
            }

            await RegisterWithHostAsync(analyzers, cancellationToken);

            LogLoadedAnalyzers(_logger, analyzers.Length, assemblyPath);

            return analyzers;
        }
        catch (Exception ex)
        {
            LogErrorLoadingAnalyzers(_logger, ex, assemblyPath);
            return ImmutableArray<IAnalyzer>.Empty;
        }
    }

    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogDiscoveringAssemblies(_logger, directory);

            var assemblyPaths = _loader.DiscoverAnalyzerAssemblies(directory, recursive: true);
            var allAnalyzers = new List<IAnalyzer>();

            foreach (var assemblyPath in assemblyPaths)
            {
                var analyzers = await LoadAnalyzersAsync(assemblyPath, cancellationToken);
                allAnalyzers.AddRange(analyzers);
            }

            LogLoadedTotalAnalyzers(_logger, allAnalyzers.Count, directory);

            return allAnalyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogErrorLoadingFromDirectory(_logger, ex, directory);
            return ImmutableArray<IAnalyzer>.Empty;
        }
    }

    public async Task<AnalyzerRunResult> RunAnalyzersAsync(
        string targetPath,
        IEnumerable<string>? analyzerIds = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            LogRunningAnalyzersOn(_logger, targetPath);

            // Auto-load analyzers if not already loaded
            await EnsureAnalyzersAutoLoadedAsync(cancellationToken);

            // Determine which analyzers to run
            ImmutableArray<IAnalyzer> analyzersToRun;

            lock (_lock)
            {
                if (analyzerIds != null && analyzerIds.Any())
                {
                    // Run specific analyzers
                    analyzersToRun = analyzerIds
                        .Where(id => _loadedAnalyzers.ContainsKey(id))
                        .Select(id => _loadedAnalyzers[id])
                        .ToImmutableArray();
                }
                else
                {
                    // Run all loaded analyzers
                    analyzersToRun = _loadedAnalyzers.Values.ToImmutableArray();
                }
            }

            if (!analyzersToRun.Any())
            {
                return new AnalyzerRunResult
                {
                    Success = false,
                    ErrorMessage = "No analyzers loaded or specified. The analyzers the open project references are loaded automatically; load others with load_roslyn_analyzers.",
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }

            LogRunningAnalyzerCount(_logger, analyzersToRun.Length);

            // Run each analyzer
            var allIssues = new List<AnalyzerIssue>();
            var analysisResults = new List<AnalysisResult>();

            foreach (var analyzerId in analyzersToRun.Select(a => a.Id))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var analysisStartTime = DateTime.UtcNow;

                    // Use the analyzer host to run the analyzer
                    var result = await _analyzerHost.RunAnalyzerAsync(
                        analyzerId,
                        targetPath,
                        options: null,
                        cancellationToken);

                    if (result.Success && result.Findings != null)
                    {
                        // Convert Findings to AnalyzerIssues
                        var issues = result.Findings.Select(f => new AnalyzerIssue
                        {
                            RuleId = f.RuleId ?? string.Empty,
                            AnalyzerId = analyzerId,
                            Severity = f.Severity switch
                            {
                                FindingSeverity.Info => IssueSeverity.Info,
                                FindingSeverity.Warning => IssueSeverity.Warning,
                                FindingSeverity.Error => IssueSeverity.Error,
                                FindingSeverity.Critical => IssueSeverity.Error,
                                _ => IssueSeverity.Info
                            },
                            Title = f.Message,
                            Description = f.Message,
                            FilePath = f.FilePath ?? targetPath,
                            LineNumber = f.LineNumber ?? 0,
                            ColumnNumber = f.ColumnNumber ?? 0
                        }).ToImmutableArray();

                        allIssues.AddRange(issues);

                        // Create AnalysisResult
                        analysisResults.Add(new AnalysisResult
                        {
                            FilePath = targetPath,
                            AnalyzerId = analyzerId,
                            StartTime = analysisStartTime,
                            EndTime = DateTime.UtcNow,
                            Success = true,
                            Issues = issues
                        });

                        LogAnalyzerFoundIssues(_logger, analyzerId, result.Findings.Count);
                    }
                    else
                    {
                        // Add failed result
                        analysisResults.Add(new AnalysisResult
                        {
                            FilePath = targetPath,
                            AnalyzerId = analyzerId,
                            StartTime = analysisStartTime,
                            EndTime = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = result.ErrorMessage
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    LogAnalyzerRunCancelled(_logger);
                    throw;
                }
                catch (Exception ex)
                {
                    LogErrorRunningAnalyzer(_logger, ex, analyzerId);
                }
            }

            return new AnalyzerRunResult
            {
                Success = true,
                AnalyzersRun = analyzersToRun.Select(a => a.Id).ToImmutableArray(),
                TotalIssuesFound = allIssues.Count,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Results = analysisResults.ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["AnalyzersRun"] = analyzersToRun.Length,
                    ["TotalIssuesFound"] = allIssues.Count,
                    ["Duration"] = DateTime.UtcNow - startTime
                }.ToImmutableDictionary()
            };
        }
        catch (OperationCanceledException)
        {
            return new AnalyzerRunResult
            {
                Success = false,
                ErrorMessage = "Analysis cancelled",
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogErrorRunningAnalyzers(_logger, ex, targetPath);

            return new AnalyzerRunResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }
    }

    public ImmutableArray<IAnalyzer> GetLoadedAnalyzers()
    {
        lock (_lock)
        {
            return _loadedAnalyzers.Values.ToImmutableArray();
        }
    }

    /// <inheritdoc />
    public Task EnsureAnalyzersLoadedAsync(CancellationToken cancellationToken = default) =>
        EnsureAnalyzersAutoLoadedAsync(cancellationToken);

    /// <summary>
    /// Auto-load the open project's analyzers now when its auto-load configuration sets <c>loadOnStartup</c>;
    /// otherwise do nothing, and they are loaded on first use
    /// </summary>
    public async Task LoadOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (_autoLoadService?.GetConfiguration() is { AutoLoadEnabled: true, LoadOnStartup: true })
        {
            await EnsureAnalyzersAutoLoadedAsync(cancellationToken);
        }
    }

    public IAnalyzer? GetAnalyzer(string analyzerId)
    {
        lock (_lock)
        {
            return _loadedAnalyzers.TryGetValue(analyzerId, out var analyzer) ? analyzer : null;
        }
    }

    /// <summary>
    /// Ensure analyzers are auto-loaded if auto-load service is available
    /// </summary>
    private async Task EnsureAnalyzersAutoLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_autoLoadService == null)
        {
            LogAutoLoadUnavailable(_logger);
            return;
        }

        try
        {
            var result = await _autoLoadService.EnsureAnalyzersLoadedAsync(cancellationToken);

            if (!result.AlreadyLoaded)
            {
                ReplaceAutoLoadedAnalyzers(result.LoadedAnalyzers);
                LogAutoLoaded(_logger, result.LoadedAnalyzers.Length, result.LoadDuration.TotalMilliseconds);
            }

            if (!result.Success && _logger.IsEnabled(LogLevel.Warning))
            {
                LogAutoLoadFailed(_logger, result.Message, string.Join("; ", result.Errors));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogErrorDuringAutoLoad(_logger, ex);
        }
    }

    /// <summary>
    /// Register explicitly loaded analyzers in the registry the analyzer host runs analyzers from. As in
    /// <see cref="_loadedAnalyzers"/>, an explicitly loaded analyzer replaces one already registered under its ID
    /// (e.g. an auto-loaded one).
    /// </summary>
    private async Task RegisterWithHostAsync(ImmutableArray<IAnalyzer> analyzers, CancellationToken cancellationToken)
    {
        if (_registry == null)
        {
            return;
        }

        foreach (var analyzer in analyzers)
        {
            var registered = _registry.GetAnalyzer(analyzer.Id);
            if (registered != null && !ReferenceEquals(registered, analyzer))
            {
                await _registry.UnregisterAnalyzerAsync(analyzer.Id, cancellationToken);
            }

            await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken);
        }
    }

    /// <summary>
    /// A new auto-load replaces the analyzers of the previous one (e.g. those of a project that is no longer
    /// open); analyzers loaded explicitly stay
    /// </summary>
    private void ReplaceAutoLoadedAnalyzers(ImmutableArray<IAnalyzer> analyzers)
    {
        lock (_lock)
        {
            foreach (var previous in _autoLoadedAnalyzers)
            {
                if (_loadedAnalyzers.TryGetValue(previous.Id, out var current) && ReferenceEquals(current, previous))
                {
                    _loadedAnalyzers.Remove(previous.Id);
                }
            }

            _autoLoadedAnalyzers = analyzers.Where(analyzer => _loadedAnalyzers.TryAdd(analyzer.Id, analyzer)).ToImmutableArray();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading Roslyn analyzers from {AssemblyPath}")]
    private static partial void LogLoadingAnalyzers(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from {AssemblyPath}")]
    private static partial void LogLoadedAnalyzers(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from {AssemblyPath}")]
    private static partial void LogErrorLoadingAnalyzers(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovering analyzer assemblies in {Directory}")]
    private static partial void LogDiscoveringAssemblies(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} total analyzers from {Directory}")]
    private static partial void LogLoadedTotalAnalyzers(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from directory {Directory}")]
    private static partial void LogErrorLoadingFromDirectory(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running analyzers on {TargetPath}")]
    private static partial void LogRunningAnalyzersOn(ILogger logger, string targetPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running {Count} analyzers")]
    private static partial void LogRunningAnalyzerCount(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzer {AnalyzerId} found {Count} issues")]
    private static partial void LogAnalyzerFoundIssues(ILogger logger, string analyzerId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer run cancelled")]
    private static partial void LogAnalyzerRunCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzer {AnalyzerId}")]
    private static partial void LogErrorRunningAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzers on {TargetPath}")]
    private static partial void LogErrorRunningAnalyzers(ILogger logger, Exception exception, string targetPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-load service not available, skipping auto-load")]
    private static partial void LogAutoLoadUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-loaded {Count} analyzers in {Duration}ms")]
    private static partial void LogAutoLoaded(ILogger logger, int count, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auto-load failed: {Message}. Errors: {Errors}")]
    private static partial void LogAutoLoadFailed(ILogger logger, string? message, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during analyzer auto-load")]
    private static partial void LogErrorDuringAutoLoad(ILogger logger, Exception exception);
}

/// <summary>
/// Result of running analyzers
/// </summary>
public class AnalyzerRunResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<string> AnalyzersRun { get; init; } = ImmutableArray<string>.Empty;
    public int TotalIssuesFound { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public ImmutableDictionary<string, object>? Statistics { get; init; }
    public ImmutableArray<AnalysisResult> Results { get; init; } = ImmutableArray<AnalysisResult>.Empty;
}
