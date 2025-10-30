using System.Collections.Immutable;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Registry for analyzer discovery and management
/// </summary>
public interface IAnalyzerRegistry
{
    /// <summary>
    /// Register an analyzer
    /// </summary>
    Task<bool> RegisterAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregister an analyzer
    /// </summary>
    Task<bool> UnregisterAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all registered analyzers
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetRegisteredAnalyzers();

    /// <summary>
    /// Get all loaded analyzers as IAnalyzer instances
    /// </summary>
    List<IAnalyzer> GetLoadedAnalyzers();

    /// <summary>
    /// Get analyzer by ID
    /// </summary>
    IAnalyzer? GetAnalyzer(string analyzerId);

    /// <summary>
    /// Get analyzer info by ID
    /// </summary>
    AnalyzerInfo? GetAnalyzerInfo(string analyzerId);

    /// <summary>
    /// Discover analyzers in directory
    /// </summary>
    Task<ImmutableArray<AnalyzerInfo>> DiscoverAnalyzersAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get analyzers that support file extension
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetAnalyzersForExtension(string extension);

    /// <summary>
    /// Get analyzers by category
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetAnalyzersByCategory(RuleCategory category);

    /// <summary>
    /// Get built-in analyzers
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetBuiltInAnalyzers();

    /// <summary>
    /// Get external analyzers
    /// </summary>
    ImmutableArray<AnalyzerInfo> GetExternalAnalyzers();

    /// <summary>
    /// Validate analyzer compatibility
    /// </summary>
    Task<CompatibilityResult> ValidateCompatibilityAsync(string assemblyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get analyzer dependencies
    /// </summary>
    Task<ImmutableArray<AnalyzerDependency>> GetDependenciesAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if analyzer dependencies are satisfied
    /// </summary>
    Task<bool> AreDependenciesSatisfiedAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when analyzer is registered
    /// </summary>
    event EventHandler<AnalyzerRegisteredEventArgs>? AnalyzerRegistered;

    /// <summary>
    /// Event fired when analyzer is unregistered
    /// </summary>
    event EventHandler<AnalyzerUnregisteredEventArgs>? AnalyzerUnregistered;
}

/// <summary>
/// Result of compatibility validation
/// </summary>
public record CompatibilityResult
{
    public bool IsCompatible { get; init; }
    public Version RequiredVersion { get; init; } = new();
    public Version ActualVersion { get; init; } = new();
    public ImmutableArray<string> MissingDependencies { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Conflicts { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Analyzer dependency information
/// </summary>
public record AnalyzerDependency
{
    public string Name { get; init; } = string.Empty;
    public Version Version { get; init; } = new();
    public bool IsOptional { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Event args for analyzer registered
/// </summary>
public record AnalyzerRegisteredEventArgs(string AnalyzerId, AnalyzerInfo AnalyzerInfo);

/// <summary>
/// Event args for analyzer unregistered
/// </summary>
public record AnalyzerUnregisteredEventArgs(string AnalyzerId);