namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of analyzing call chains (who calls who, and who calls them)
/// </summary>
public class CallChainResult
{
    public required MethodSignature TargetMethod { get; init; }
    public CallDirection Direction { get; init; }
    public List<CallChainPath> Paths { get; init; } = new();
    public int TotalPaths => Paths.Count;
    public DateTime AnalysisTime { get; init; } = DateTime.UtcNow;
    public CallChainMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Get paths by confidence level
    /// </summary>
    public Dictionary<ConfidenceLevel, List<CallChainPath>> PathsByConfidence =>
        Paths.GroupBy(p => p.Confidence)
             .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Get shortest paths
    /// </summary>
    public List<CallChainPath> ShortestPaths =>
        Paths.Where(p => p.Length == Paths.Min(x => x.Length)).ToList();

    /// <summary>
    /// Get longest paths
    /// </summary>
    public List<CallChainPath> LongestPaths =>
        Paths.Where(p => p.Length == Paths.Max(x => x.Length)).ToList();

    /// <summary>
    /// Get unique calling methods (direct callers)
    /// </summary>
    public List<MethodSignature> UniqueCallers =>
        Paths.Where(p => p.Length > 0)
             .Select(p => p.Steps.First().FromMethod)
             .Distinct()
             .ToList();

    /// <summary>
    /// Get unique called methods (for forward analysis)
    /// </summary>
    public List<MethodSignature> UniqueCallees =>
        Paths.Where(p => p.Length > 0)
             .Select(p => p.Steps.Last().ToMethod)
             .Distinct()
             .ToList();
}

/// <summary>
/// A single call chain path
/// </summary>
public class CallChainPath
{
    public required List<CallChainStep> Steps { get; init; } = new();
    public required ConfidenceLevel Confidence { get; init; }
    public int Length => Steps.Count;
    public bool IsRecursive { get; init; }
    public List<string> LoopDetected { get; init; } = new();
    public string? PathDescription { get; init; }

    /// <summary>
    /// Get the starting method of this path
    /// </summary>
    public MethodSignature? StartMethod => Steps.FirstOrDefault()?.FromMethod;

    /// <summary>
    /// Get the ending method of this path
    /// </summary>
    public MethodSignature? EndMethod => Steps.LastOrDefault()?.ToMethod;

    /// <summary>
    /// Get human-readable description of the path
    /// </summary>
    public string GetDescription()
    {
        if (!string.IsNullOrEmpty(PathDescription))
            return PathDescription;

        if (Steps.Count == 0)
            return "Empty path";

        var methodNames = Steps.Select(s => $"{s.FromMethod.Name} -> {s.ToMethod.Name}");
        return string.Join(" -> ", methodNames);
    }
}

/// <summary>
/// Single step in a call chain
/// </summary>
public class CallChainStep
{
    public required MethodSignature FromMethod { get; init; }
    public required MethodSignature ToMethod { get; init; }
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required CallType CallType { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public string? CallExpression { get; init; }
    public string? Context { get; init; }

    /// <summary>
    /// Location of the call
    /// </summary>
    public string Location => $"{Path.GetFileName(File)}:{Line + 1}:{Column + 1}";
}

/// <summary>
/// Metadata about the call chain analysis
/// </summary>
public class CallChainMetadata
{
    public TimeSpan AnalysisDuration { get; init; }
    public int MaxDepth { get; init; }
    public int MethodsAnalyzed { get; init; }
    public int FilesAnalyzed { get; init; }
    public List<string> AnalysisWarnings { get; init; } = new();
    public bool ReachedMaxDepth { get; init; }
    public bool IncompleteAnalysis { get; init; }
    public string? AnalysisMethod { get; init; }
    public Dictionary<string, object> AdditionalData { get; init; } = new();
}

/// <summary>
/// Direction of call chain analysis
/// </summary>
public enum CallDirection
{
    /// <summary>
    /// Find who calls this method (backward analysis)
    /// </summary>
    Backward,

    /// <summary>
    /// Find what this method calls (forward analysis)
    /// </summary>
    Forward,

    /// <summary>
    /// Both directions (full call graph)
    /// </summary>
    Both
}