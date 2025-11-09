using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

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
}

public class RoslynAnalyzerService : IRoslynAnalyzerService
{
    private readonly RoslynAnalyzerLoader _loader;
    private readonly IAnalyzerHost _analyzerHost;
    private readonly ILogger<RoslynAnalyzerService> _logger;
    private readonly AnalyzerAutoLoadService? _autoLoadService;
    private readonly Dictionary<string, IAnalyzer> _loadedAnalyzers = new();
    private readonly object _lock = new();

    public RoslynAnalyzerService(
        RoslynAnalyzerLoader loader,
        IAnalyzerHost analyzerHost,
        ILogger<RoslynAnalyzerService> logger,
        AnalyzerAutoLoadService? autoLoadService = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _analyzerHost = analyzerHost ?? throw new ArgumentNullException(nameof(analyzerHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _autoLoadService = autoLoadService;
    }

    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading Roslyn analyzers from {AssemblyPath}", assemblyPath);

            var analyzers = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);

            // Register loaded analyzers
            lock (_lock)
            {
                foreach (var analyzer in analyzers)
                {
                    _loadedAnalyzers[analyzer.Id] = analyzer;
                }
            }

            _logger.LogInformation("Loaded {Count} analyzers from {AssemblyPath}",
                analyzers.Length, assemblyPath);

            return analyzers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzers from {AssemblyPath}", assemblyPath);
            return ImmutableArray<IAnalyzer>.Empty;
        }
    }

    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering analyzer assemblies in {Directory}", directory);

            var assemblyPaths = _loader.DiscoverAnalyzerAssemblies(directory, recursive: true);
            var allAnalyzers = new List<IAnalyzer>();

            foreach (var assemblyPath in assemblyPaths)
            {
                var analyzers = await LoadAnalyzersAsync(assemblyPath, cancellationToken);
                allAnalyzers.AddRange(analyzers);
            }

            _logger.LogInformation("Loaded {Count} total analyzers from {Directory}",
                allAnalyzers.Count, directory);

            return allAnalyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzers from directory {Directory}", directory);
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
            _logger.LogInformation("Running analyzers on {TargetPath}", targetPath);

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
                    ErrorMessage = "No analyzers loaded or specified. Auto-load may have failed - check logs.",
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }

            _logger.LogInformation("Running {Count} analyzers", analyzersToRun.Length);

            // Run each analyzer
            var allIssues = new List<AnalyzerIssue>();
            var analysisResults = new List<AnalysisResult>();

            foreach (var analyzer in analyzersToRun)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var analysisStartTime = DateTime.UtcNow;

                    // Use the analyzer host to run the analyzer
                    var result = await _analyzerHost.RunAnalyzerAsync(
                        analyzer.Id,
                        targetPath,
                        options: null,
                        cancellationToken);

                    if (result.Success && result.Findings != null)
                    {
                        // Convert Findings to AnalyzerIssues
                        var issues = result.Findings.Select(f => new AnalyzerIssue
                        {
                            RuleId = f.Id,
                            AnalyzerId = analyzer.Id,
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
                            AnalyzerId = analyzer.Id,
                            StartTime = analysisStartTime,
                            EndTime = DateTime.UtcNow,
                            Success = true,
                            Issues = issues
                        });

                        _logger.LogDebug("Analyzer {AnalyzerId} found {Count} issues",
                            analyzer.Id, result.Findings.Count);
                    }
                    else
                    {
                        // Add failed result
                        analysisResults.Add(new AnalysisResult
                        {
                            FilePath = targetPath,
                            AnalyzerId = analyzer.Id,
                            StartTime = analysisStartTime,
                            EndTime = DateTime.UtcNow,
                            Success = false,
                            ErrorMessage = result.ErrorMessage
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Analyzer run cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error running analyzer {AnalyzerId}", analyzer.Id);
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
            _logger.LogError(ex, "Error running analyzers on {TargetPath}", targetPath);

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
            _logger.LogDebug("Auto-load service not available, skipping auto-load");
            return;
        }

        try
        {
            var result = await _autoLoadService.EnsureAnalyzersLoadedAsync(cancellationToken);

            if (result.Success && !result.AlreadyLoaded)
            {
                // Register auto-loaded analyzers with this service
                lock (_lock)
                {
                    foreach (var analyzer in result.LoadedAnalyzers)
                    {
                        if (!_loadedAnalyzers.ContainsKey(analyzer.Id))
                        {
                            _loadedAnalyzers[analyzer.Id] = analyzer;
                        }
                    }
                }

                _logger.LogInformation("Auto-loaded {Count} analyzers in {Duration}ms",
                    result.LoadedAnalyzers.Length,
                    result.LoadDuration.TotalMilliseconds);
            }
            else if (!result.Success)
            {
                _logger.LogWarning("Auto-load failed: {Message}. Errors: {Errors}",
                    result.Message,
                    string.Join("; ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during analyzer auto-load");
        }
    }
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
