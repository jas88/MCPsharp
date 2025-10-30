using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing call chains (who calls who, and who calls them)
/// </summary>
public interface ICallChainService
{
    /// <summary>
    /// Find call chains leading to a specific method (backward analysis)
    /// </summary>
    Task<CallChainResult?> FindCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find call chains from a specific method (forward analysis)
    /// </summary>
    Task<CallChainResult?> FindForwardCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find call chains for a method at a specific location
    /// </summary>
    Task<CallChainResult?> FindCallChainsAtLocationAsync(string filePath, int line, int column, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find call chains for a specific symbol
    /// </summary>
    Task<CallChainResult?> FindCallChainsAsync(ISymbol symbol, CallDirection direction = CallDirection.Backward, int maxDepth = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find call chains between two specific methods
    /// </summary>
    Task<List<CallChainPath>> FindCallChainsBetweenAsync(MethodSignature fromMethod, MethodSignature toMethod, int maxDepth = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find recursive call chains
    /// </summary>
    Task<List<CallChainPath>> FindRecursiveCallChainsAsync(string methodName, string? containingType = null, int maxDepth = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze call graph for a specific type or namespace
    /// </summary>
    Task<CallGraphAnalysis> AnalyzeCallGraphAsync(string? typeName = null, string? namespaceName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find circular dependencies in call chains
    /// </summary>
    Task<List<CircularDependency>> FindCircularDependenciesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get shortest path between two methods
    /// </summary>
    Task<CallChainPath?> FindShortestPathAsync(MethodSignature fromMethod, MethodSignature toMethod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all methods reachable from a starting point
    /// </summary>
    Task<ReachabilityAnalysis> FindReachableMethodsAsync(string methodName, string? containingType = null, int maxDepth = 10, CancellationToken cancellationToken = default);
}

/// <summary>
/// Analysis of a call graph
/// </summary>
public class CallGraphAnalysis
{
    public required string Scope { get; init; }
    public required List<MethodSignature> Methods { get; init; }
    public required List<CallRelationship> Relationships { get; init; }
    public required Dictionary<string, List<string>> CallGraph { get; init; }
    public required Dictionary<string, List<string>> ReverseCallGraph { get; init; }
    public required List<string> EntryPoints { get; init; }
    public required List<string> LeafMethods { get; init; }
    public required List<CircularDependency> CircularDependencies { get; init; }
    public int MaxCallDepth { get; init; }
    public double AverageCallDepth { get; init; }
}

/// <summary>
/// Represents a call relationship between methods
/// </summary>
public class CallRelationship
{
    public required MethodSignature FromMethod { get; init; }
    public required MethodSignature ToMethod { get; init; }
    public required CallType CallType { get; init; }
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required int CallCount { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
}

/// <summary>
/// Represents a circular dependency
/// </summary>
public class CircularDependency
{
    public required List<MethodSignature> Methods { get; init; }
    public required int CycleLength { get; init; }
    public required List<CallChainStep> Steps { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required List<string> FilesInvolved { get; init; }

    /// <summary>
    /// Get description of the circular dependency
    /// </summary>
    public string GetDescription()
    {
        var methodNames = Methods.Select(m => $"{m.ContainingType}.{m.Name}");
        return $"Circular dependency: {string.Join(" -> ", methodNames)} -> {methodNames.First()}";
    }
}

/// <summary>
/// Analysis of reachable methods from a starting point
/// </summary>
public class ReachabilityAnalysis
{
    public required MethodSignature StartMethod { get; init; }
    public required List<MethodSignature> ReachableMethods { get; init; }
    public required Dictionary<int, List<MethodSignature>> MethodsByDepth { get; init; }
    public required List<MethodSignature> UnreachableMethods { get; init; }
    public required int MaxDepthReached { get; init; }
    public required int TotalMethodsAnalyzed { get; init; }
    public required TimeSpan AnalysisDuration { get; init; }
    public bool ReachedAnalysisLimit { get; init; }
}