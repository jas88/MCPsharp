namespace MCPsharp.Models.LargeFileOptimization;

/// <summary>
/// Result of optimizing a large method
/// </summary>
public class MethodOptimizationResult
{
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string FilePath { get; init; }
    /// <summary>1-based first line of the method declaration (0 when unknown).</summary>
    public int StartLine { get; init; }
    /// <summary>1-based last line of the method declaration (0 when unknown).</summary>
    public int EndLine { get; init; }
    public required MethodMetrics CurrentMetrics { get; init; }
    public required List<MethodRefactoringStrategy> RefactoringStrategies { get; init; } = new();
    public required List<CodeExample> BeforeAfterExamples { get; init; } = new();
    public required OptimizationPriority Priority { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required double ExpectedBenefit { get; init; }
}

/// <summary>
/// Metrics for a method
/// </summary>
public class MethodMetrics
{
    public required int LineCount { get; init; }
    public required int ParameterCount { get; init; }
    public required int LocalVariableCount { get; init; }
    public required int LoopCount { get; init; }
    public required int ConditionalCount { get; init; }
    public required int TryCatchCount { get; init; }
    public required int MaximumNestingDepth { get; init; }
    public required ComplexityMetrics Complexity { get; init; }
    public required GodMethodAnalysis GodMethodScore { get; init; }
    public required bool IsTooLarge { get; init; }
    public required bool HasTooManyParameters { get; init; }
    public required bool IsTooComplex { get; init; }
}

/// <summary>
/// Strategy for refactoring a large method
/// </summary>
public class MethodRefactoringStrategy
{
    public required string StrategyName { get; init; }
    public required string Description { get; init; }
    public required MethodRefactoringType RefactoringType { get; init; }
    public required List<string> TargetLines { get; init; } = new();
    public required double Confidence { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required List<string> Pros { get; init; } = new();
    public required List<string> Cons { get; init; } = new();
}

/// <summary>
/// Types of method refactoring strategies
/// </summary>
public enum MethodRefactoringType
{
    ExtractMethod,          // Extract parts into separate methods
    ExtractClass,          // Extract method into new class
    ReplaceConditional,    // Replace complex conditionals with polymorphism
    IntroduceParameterObject, // Group parameters into objects
    DecomposeConditional,  // Break down complex conditionals
    ReplaceMagicNumber,    // Replace magic numbers with constants
    ExtractStrategy,       // Extract algorithm into strategy pattern
    SimplifyConditional,   // Simplify nested conditionals
}

/// <summary>
/// Analysis of a God method
/// </summary>
public class GodMethodAnalysis
{
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string FilePath { get; init; }
    public required double GodMethodScore { get; init; }
    public required GodMethodSeverity Severity { get; init; }
    public required List<string> Violations { get; init; } = new();
    public required List<string> TooManyParameters { get; init; } = new();
    public required List<string> TooManyLocals { get; init; } = new();
    public required List<string> HighComplexityBlocks { get; init; } = new();
    public required List<MethodRefactoringStrategy> RecommendedRefactorings { get; init; } = new();
}

/// <summary>
/// Severity levels for God method detection
/// </summary>
public enum GodMethodSeverity
{
    None,       // 0-0.3
    Low,        // 0.3-0.5
    Medium,     // 0.5-0.7
    High,       // 0.7-0.9
    Critical    // > 0.9
}
