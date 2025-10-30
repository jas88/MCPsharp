using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing who calls a specific method or symbol
/// </summary>
public interface ICallerAnalysisService
{
    /// <summary>
    /// Find all callers of a specific method
    /// </summary>
    Task<CallerResult?> FindCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all callers of a method at a specific location
    /// </summary>
    Task<CallerResult?> FindCallersAtLocationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all callers of a specific symbol
    /// </summary>
    Task<CallerResult?> FindCallersAsync(ISymbol symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all callers with specific method signature matching
    /// </summary>
    Task<CallerResult?> FindCallersBySignatureAsync(MethodSignature signature, bool exactMatch = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find direct callers only (no indirect calls through interfaces/base classes)
    /// </summary>
    Task<CallerResult?> FindDirectCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find indirect callers (through interfaces, base classes, delegates)
    /// </summary>
    Task<CallerResult?> FindIndirectCallersAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze call patterns for a method (how it's typically called)
    /// </summary>
    Task<CallPatternAnalysis> AnalyzeCallPatternsAsync(string methodName, string? containingType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find potential dead code (methods that are never called)
    /// </summary>
    Task<List<MethodSignature>> FindUnusedMethodsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find methods that are only called by tests
    /// </summary>
    Task<List<MethodSignature>> FindTestOnlyMethodsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Analysis of call patterns for a method
/// </summary>
public class CallPatternAnalysis
{
    public required MethodSignature TargetMethod { get; init; }
    public List<CallPattern> Patterns { get; init; } = new();
    public int TotalCallSites { get; init; }
    public Dictionary<string, int> CallFrequencyByFile { get; init; } = new();
    public List<string> CommonCallContexts { get; init; } = new();
    public bool HasRecursiveCalls { get; init; }
    public bool IsCalledAsynchronously { get; init; }
    public bool IsCalledInLoops { get; init; }
    public bool IsCalledInExceptionHandlers { get; init; }
}

/// <summary>
/// A specific call pattern
/// </summary>
public class CallPattern
{
    public required string Pattern { get; init; }
    public required int Frequency { get; init; }
    public required List<string> Contexts { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
}