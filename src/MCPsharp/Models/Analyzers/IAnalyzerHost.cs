namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Host for managing analyzers
/// </summary>
public interface IAnalyzerHost
{
    /// <summary>
    /// Load an analyzer from assembly
    /// </summary>
    Task<AnalyzerLoadResult> LoadAnalyzerAsync(string assemblyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unload an analyzer
    /// </summary>
    Task<bool> UnloadAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all loaded analyzers
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetLoadedAnalyzers();

    /// <summary>
    /// Get analyzer by ID
    /// </summary>
    IAnalyzer? GetAnalyzer(string analyzerId);

    /// <summary>
    /// Get analyzer info by ID
    /// </summary>
    AnalyzerInfo? GetAnalyzerInfo(string analyzerId);

    /// <summary>
    /// Enable or disable an analyzer
    /// </summary>
    Task<bool> SetAnalyzerEnabledAsync(string analyzerId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configure an analyzer
    /// </summary>
    Task<bool> ConfigureAnalyzerAsync(string analyzerId, AnalyzerConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run analysis on files
    /// </summary>
    Task<AnalysisSessionResult> RunAnalysisAsync(AnalysisRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available fixes for issues
    /// </summary>
    Task<ImmutableArray<AnalyzerFix>> GetFixesAsync(string analyzerId, ImmutableArray<string> issueIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply fixes
    /// </summary>
    Task<FixSessionResult> ApplyFixesAsync(ApplyFixRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get health status of all analyzers
    /// </summary>
    Task<ImmutableArray<AnalyzerHealthStatus>> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when an analyzer is loaded
    /// </summary>
    event EventHandler<AnalyzerLoadedEventArgs>? AnalyzerLoaded;

    /// <summary>
    /// Event fired when an analyzer is unloaded
    /// </summary>
    event EventHandler<AnalyzerUnloadedEventArgs>? AnalyzerUnloaded;

    /// <summary>
    /// Event fired when analysis completes
    /// </summary>
    event EventHandler<AnalysisCompletedEventArgs>? AnalysisCompleted;
}

/// <summary>
/// Result of loading an analyzer
/// </summary>
public record AnalyzerLoadResult
{
    public bool Success { get; init; }
    public string AnalyzerId { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public SecurityValidationResult? SecurityValidation { get; init; }
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Result of an analysis session
/// </summary>
public record AnalysisSessionResult
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public string AnalyzerId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<AnalysisResult> Results { get; init; } = ImmutableArray<AnalysisResult>.Empty;
    public ImmutableArray<AnalyzerIssue> AllIssues { get; init; } = ImmutableArray<AnalyzerIssue>.Empty;
    public ImmutableDictionary<string, object> Statistics { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Result of a fix session
/// </summary>
public record FixSessionResult
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public string AnalyzerId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<FixResult> Results { get; init; } = ImmutableArray<FixResult>.Empty;
    public ImmutableArray<string> ModifiedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Conflicts { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableDictionary<string, object> Statistics { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Health status of an analyzer
/// </summary>
public record AnalyzerHealthStatus
{
    public string AnalyzerId { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public bool IsLoaded { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime LastActivity { get; init; } = DateTime.UtcNow;
    public TimeSpan Uptime { get; init; }
    public int AnalysesRun { get; init; }
    public int ErrorsEncountered { get; init; }
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public string? LastError { get; init; }
    public ImmutableDictionary<string, object> Metrics { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Event args for analyzer loaded
/// </summary>
public record AnalyzerLoadedEventArgs(string AnalyzerId, AnalyzerInfo AnalyzerInfo);

/// <summary>
/// Event args for analyzer unloaded
/// </summary>
public record AnalyzerUnloadedEventArgs(string AnalyzerId);

/// <summary>
/// Event args for analysis completed
/// </summary>
public record AnalysisCompletedEventArgs(string SessionId, AnalysisSessionResult Result);