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
    /// <summary>
    /// Run analyzers on a file, or on the C# files of a directory: each file gets the analyzers that apply to it
    /// (see <see cref="GetAnalyzersForFile"/>), narrowed to <paramref name="analyzerIds"/> when given.
    /// </summary>
    Task<AnalyzerRunResult> RunAnalyzersAsync(string targetPath, IEnumerable<string>? analyzerIds = null, CancellationToken cancellationToken = default);
    ImmutableArray<IAnalyzer> GetLoadedAnalyzers();

    /// <summary>
    /// The loaded analyzers that apply to a file, as in an IDE: those of the projects that compile it, and those
    /// loaded with load_roslyn_analyzers or from customAnalyzerPaths, which belong to no project.
    /// </summary>
    ImmutableArray<IAnalyzer> GetAnalyzersForFile(string filePath);
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
    /// The analyzer host's registry. Explicitly loaded analyzers are registered there so the host can run them, and
    /// an analyzer is listed as loaded only while the registry holds it; without a registry, analyzers are listed
    /// but not run.
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
        if (registry != null)
        {
            // Whatever unregisters an analyzer (an unload, or an auto-load replacing or abandoning its analyzers), it
            // is no longer listed as loaded
            registry.AnalyzerUnregistered += (_, args) => StopListingUnregisteredAnalyzer(args.AnalyzerId);
        }
    }

    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogLoadingAnalyzers(_logger, assemblyPath);

            var analyzers = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);

            // Only an analyzer the host's registry holds can run, so only those are published as loaded
            var registered = await RegisterWithHostAsync(analyzers, cancellationToken);
            var loaded = PublishRegistered(registered, published =>
            {
                lock (_lock)
                {
                    foreach (var analyzer in published)
                    {
                        _loadedAnalyzers[analyzer.Id] = analyzer;
                    }
                }
            });

            LogLoadedAnalyzers(_logger, loaded.Length, assemblyPath);

            return loaded;
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
            // A load's analyzers are listed here before the auto-load lets anyone else go on, so a caller that waited
            // for the load (e.g. a request made during the load at startup) finds them listed
            var result = await _autoLoadService.EnsureAnalyzersLoadedAsync(
                loaded => ReplaceAutoLoadedAnalyzers(loaded.LoadedAnalyzers),
                cancellationToken);

            if (!result.AlreadyLoaded)
            {
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
    /// Register explicitly loaded analyzers in the registry the analyzer host runs analyzers from, returning those
    /// registered. An explicitly loaded analyzer replaces one already registered under its ID (e.g. an auto-loaded
    /// one) in one step, and one that fails to register leaves the previous analyzer in place. Without a registry,
    /// every analyzer is returned, to be listed but not run.
    /// </summary>
    private async Task<ImmutableArray<IAnalyzer>> RegisterWithHostAsync(ImmutableArray<IAnalyzer> analyzers, CancellationToken cancellationToken)
    {
        if (_registry == null)
        {
            return analyzers;
        }

        var registered = ImmutableArray.CreateBuilder<IAnalyzer>(analyzers.Length);
        foreach (var analyzer in analyzers)
        {
            try
            {
                if (await _registry.ReplaceAnalyzerAsync(analyzer, cancellationToken))
                {
                    registered.Add(analyzer);
                }
                else
                {
                    LogAnalyzerNotRegistered(_logger, analyzer.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The analyzer it was to replace stays registered
                LogAnalyzerFailedToInitialize(_logger, ex, analyzer.Id);
            }
        }

        return registered.ToImmutable();
    }

    /// <summary>
    /// Publish as loaded those of <paramref name="analyzers"/>, which were registered, that the registry still holds,
    /// and return them. One replaced or unregistered since, by another load of its ID or an unload, is left out: the
    /// analyzer the registry holds under its ID, if any, is the one to list. The registry's analyzers don't change
    /// while <paramref name="publish"/> runs, so the analyzers this service lists stay in step with them. Without a
    /// registry, every analyzer is published.
    /// </summary>
    private ImmutableArray<IAnalyzer> PublishRegistered(ImmutableArray<IAnalyzer> analyzers, Action<ImmutableArray<IAnalyzer>> publish)
    {
        if (_registry == null)
        {
            publish(analyzers);
            return analyzers;
        }

        var published = _registry.PublishRegisteredAnalyzers(analyzers, publish);
        foreach (var analyzer in analyzers.Except<IAnalyzer>(published, ReferenceEqualityComparer.Instance))
        {
            LogAnalyzerNoLongerRegistered(_logger, analyzer.Id);
        }

        return published;
    }

    /// <summary>
    /// Stop listing the analyzer listed under <paramref name="analyzerId"/>, unless the registry holds it: an analyzer
    /// with that ID has been unregistered, and since then another may have been registered under the ID and listed
    /// </summary>
    private void StopListingUnregisteredAnalyzer(string analyzerId)
    {
        var listed = GetAnalyzer(analyzerId);
        if (listed == null || _registry == null)
        {
            return;
        }

        var stoppedListing = false;
        _registry.PublishRegisteredAnalyzers([listed], registered =>
        {
            if (!registered.IsEmpty)
            {
                return;
            }

            lock (_lock)
            {
                if (_loadedAnalyzers.TryGetValue(analyzerId, out var current) && ReferenceEquals(current, listed))
                {
                    stoppedListing = _loadedAnalyzers.Remove(analyzerId);
                }
            }
        });

        if (stoppedListing)
        {
            LogStoppedListingAnalyzer(_logger, analyzerId);
        }
    }

    /// <summary>
    /// A new auto-load replaces the analyzers of the previous one (e.g. those of a project that is no longer
    /// open); analyzers loaded explicitly stay
    /// </summary>
    private void ReplaceAutoLoadedAnalyzers(ImmutableArray<IAnalyzer> analyzers) =>
        PublishRegistered(analyzers, published =>
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

                _autoLoadedAnalyzers = published.Where(ListAutoLoadedAnalyzer).ToImmutableArray();
            }
        });

    /// <summary>
    /// List an auto-loaded analyzer, while holding <see cref="_lock"/>, and return whether it is listed. With a
    /// registry, the analyzer is the one the registry holds under its ID, so an analyzer already listed under the ID
    /// has been unregistered (which this service has yet to be told) and gives way. Without a registry, an analyzer
    /// already listed under the ID was loaded explicitly, and stays.
    /// </summary>
    private bool ListAutoLoadedAnalyzer(IAnalyzer analyzer)
    {
        if (_registry == null)
        {
            return _loadedAnalyzers.TryAdd(analyzer.Id, analyzer);
        }

        _loadedAnalyzers[analyzer.Id] = analyzer;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading Roslyn analyzers from {AssemblyPath}")]
    private static partial void LogLoadingAnalyzers(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from {AssemblyPath}")]
    private static partial void LogLoadedAnalyzers(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from {AssemblyPath}")]
    private static partial void LogErrorLoadingAnalyzers(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The analyzer host could not register analyzer {AnalyzerId}, so it was not loaded")]
    private static partial void LogAnalyzerNotRegistered(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analyzer {AnalyzerId} failed to initialize, so it was not loaded")]
    private static partial void LogAnalyzerFailedToInitialize(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} is not the analyzer registered under its ID any more, so it is not listed as loaded")]
    private static partial void LogAnalyzerNoLongerRegistered(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzer {AnalyzerId} was unregistered, so it is no longer listed as loaded")]
    private static partial void LogStoppedListingAnalyzer(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovering analyzer assemblies in {Directory}")]
    private static partial void LogDiscoveringAssemblies(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} total analyzers from {Directory}")]
    private static partial void LogLoadedTotalAnalyzers(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from directory {Directory}")]
    private static partial void LogErrorLoadingFromDirectory(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-load service not available, skipping auto-load")]
    private static partial void LogAutoLoadUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-loaded {Count} analyzers in {Duration}ms")]
    private static partial void LogAutoLoaded(ILogger logger, int count, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auto-load failed: {Message}. Errors: {Errors}")]
    private static partial void LogAutoLoadFailed(ILogger logger, string? message, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during analyzer auto-load")]
    private static partial void LogErrorDuringAutoLoad(ILogger logger, Exception exception);
}
