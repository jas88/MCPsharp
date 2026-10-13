namespace MCPsharp.Models.LargeFileOptimization;

/// <summary>
/// Result of optimizing a large class
/// </summary>
public class ClassOptimizationResult
{
    public required string ClassName { get; init; }
    public required string FilePath { get; init; }
    public required ClassMetrics CurrentMetrics { get; init; }
    public required List<SplittingStrategy> SplittingStrategies { get; init; } = new();
    public required List<RefactoringSuggestion> RefactoringSuggestions { get; init; } = new();
    public required List<CodeExample> BeforeAfterExamples { get; init; } = new();
    public required OptimizationPriority Priority { get; init; }
    public required List<string> RecommendedActions { get; init; } = new();
    public required int EstimatedEffortHours { get; init; }
    public required double ExpectedBenefit { get; init; }
}

/// <summary>
/// Metrics for a class
/// </summary>
public class ClassMetrics
{
    public required int LineCount { get; init; }
    public required int MethodCount { get; init; }
    public required int PropertyCount { get; init; }
    public required int FieldCount { get; init; }
    public required int ConstructorCount { get; init; }
    public required ComplexityMetrics Complexity { get; init; }
    public required List<string> Responsibilities { get; init; } = new();
    public required List<string> Dependencies { get; init; } = new();
    public required GodClassAnalysis GodClassScore { get; init; }
    public required bool IsTooLarge { get; init; }
    public required bool HasTooManyResponsibilities { get; init; }
}

/// <summary>
/// Strategy for splitting a large class
/// </summary>
public class SplittingStrategy
{
    public required string StrategyName { get; init; }
    public required string Description { get; init; }
    public required List<string> TargetMembers { get; init; } = new();
    public required string NewClassName { get; init; }
    public required SplitType SplitType { get; init; }
    public required double Confidence { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required List<string> Pros { get; init; } = new();
    public required List<string> Cons { get; init; } = new();
}

/// <summary>
/// Types of class splitting strategies
/// </summary>
public enum SplitType
{
    ExtractClass,           // Extract related functionality into a new class
    ExtractInterface,       // Extract interface for better abstraction
    ExtractDelegate,        // Extract complex logic into delegate classes
    GroupByResponsibility,  // Group methods by single responsibility
    GroupByLayer,          // Separate by architectural layers
    GroupByFeature,        // Group by feature cohesion
}

/// <summary>
/// Analysis of a God class
/// </summary>
public class GodClassAnalysis
{
    public required string ClassName { get; init; }
    public required string FilePath { get; init; }
    public required double GodClassScore { get; init; }
    public required GodClassSeverity Severity { get; init; }
    public required List<string> Violations { get; init; } = new();
    public required List<string> Responsibilities { get; init; } = new();
    public required List<string> TooManyMethods { get; init; } = new();
    public required List<string> TooManyFields { get; init; } = new();
    public required List<string> HighCouplingClasses { get; init; } = new();
    public required List<SplittingStrategy> RecommendedSplits { get; init; } = new();
}

/// <summary>
/// Severity levels for God class detection
/// </summary>
public enum GodClassSeverity
{
    None,       // 0-0.3
    Low,        // 0.3-0.5
    Medium,     // 0.5-0.7
    High,       // 0.7-0.9
    Critical    // > 0.9
}
