namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of analyzing who calls a specific method or symbol
/// </summary>
public class CallerResult
{
    public required string TargetSymbol { get; init; }
    public required MethodSignature TargetSignature { get; init; }
    public List<CallerInfo> Callers { get; init; } = new();
    public int TotalCallers => Callers.Count;
    public DateTime AnalysisTime { get; init; } = DateTime.UtcNow;
    public CallAnalysisMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Get callers by confidence level
    /// </summary>
    public Dictionary<ConfidenceLevel, List<CallerInfo>> CallersByConfidence =>
        Callers.GroupBy(c => c.Confidence)
               .ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>
    /// Get direct callers only
    /// </summary>
    public List<CallerInfo> DirectCallers => Callers.Where(c => c.CallType == CallType.Direct).ToList();

    /// <summary>
    /// Get indirect callers (through interfaces/base classes)
    /// </summary>
    public List<CallerInfo> IndirectCallers => Callers.Where(c => c.CallType == CallType.Indirect).ToList();

    /// <summary>
    /// Get callers in the same file
    /// </summary>
    public List<CallerInfo> LocalCallers => Callers.Where(c => c.File == TargetSignature.ContainingType).ToList();
}

/// <summary>
/// Information about a caller of a method
/// </summary>
public class CallerInfo
{
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required string CallerMethod { get; init; }
    public required string CallerType { get; init; }
    public required string CallExpression { get; init; }
    public required CallType CallType { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required MethodSignature? CallerSignature { get; init; }
    public string? Context { get; init; }
    public List<string> CallChain { get; init; } = new();
    public bool IsRecursive { get; init; }

    /// <summary>
    /// Location information for display
    /// </summary>
    public string Location => $"{Path.GetFileName(File)}:{Line + 1}:{Column + 1}";
}

/// <summary>
/// Metadata about the call analysis
/// </summary>
public class CallAnalysisMetadata
{
    public TimeSpan AnalysisDuration { get; init; }
    public int FilesAnalyzed { get; init; }
    public int MethodsAnalyzed { get; init; }
    public List<string> AnalysisWarnings { get; init; } = new();
    public bool IncompleteAnalysis { get; init; }
    public string? AnalysisMethod { get; init; }
    public Dictionary<string, object> AdditionalData { get; init; } = new();
}

/// <summary>
/// Type of call relationship
/// </summary>
public enum CallType
{
    /// <summary>
    /// Direct method call
    /// </summary>
    Direct,

    /// <summary>
    /// Call through interface or base class
    /// </summary>
    Indirect,

    /// <summary>
    /// Call via delegate or lambda
    /// </summary>
    Delegate,

    /// <summary>
    /// Call via reflection
    /// </summary>
    Reflection,

    /// <summary>
    /// Call through event subscription
    /// </summary>
    Event,

    /// <summary>
    /// Unknown call type
    /// </summary>
    Unknown
}

/// <summary>
/// Confidence level in the analysis result
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>
    /// High confidence - definitive match
    /// </summary>
    High = 3,

    /// <summary>
    /// Medium confidence - likely but not certain
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Low confidence - possible match
    /// </summary>
    Low = 1
}