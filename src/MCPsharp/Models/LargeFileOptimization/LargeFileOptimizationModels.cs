namespace MCPsharp.Models.LargeFileOptimization;

/// <summary>
/// Result of analyzing large files in a project
/// </summary>
public class LargeFileAnalysisResult
{
    public required string ProjectPath { get; init; }
    public required List<LargeFileInfo> LargeFiles { get; init; } = new();
    public required Dictionary<string, int> FileSizeDistribution { get; init; } = new();
    public required TimeSpan AnalysisDuration { get; init; }
    public required int TotalFilesAnalyzed { get; init; }
    public required int FilesAboveThreshold { get; init; }
    public required int AverageFileSize { get; init; }
    public required int LargestFileSize { get; init; }
    public required List<OptimizationRecommendation> GlobalRecommendations { get; init; } = new();
    public required OptimizationStatistics Statistics { get; init; }
}

/// <summary>
/// Information about a large file that needs optimization
/// </summary>
public class LargeFileInfo
{
    public required string FilePath { get; init; }
    public required int LineCount { get; init; }
    public required int CharacterCount { get; init; }
    public required FileSizeCategory SizeCategory { get; init; }
    public required List<string> LargeClasses { get; init; } = new();
    public required List<string> LargeMethods { get; init; } = new();
    public required ComplexityMetrics OverallComplexity { get; init; }
    public required List<CodeSmell> CodeSmells { get; init; } = new();
    public required double OptimizationPriority { get; init; }
    public required List<string> ImmediateActions { get; init; } = new();
}

/// <summary>
/// Categories for file sizes
/// </summary>
public enum FileSizeCategory
{
    Small,      // < 200 lines
    Medium,     // 200-500 lines
    Large,      // 500-1000 lines
    VeryLarge,  // 1000-2000 lines
    Enormous    // > 2000 lines
}

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
/// Result of optimizing a large method
/// </summary>
public class MethodOptimizationResult
{
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string FilePath { get; init; }
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
/// Complexity metrics for code analysis
/// </summary>
public class ComplexityMetrics
{
    public required int CyclomaticComplexity { get; init; }
    public required int CognitiveComplexity { get; init; }
    public required int HalsteadVolume { get; init; }
    public required int HalsteadDifficulty { get; init; }
    public required double MaintainabilityIndex { get; init; }
    public required int MaximumNestingDepth { get; init; }
    public required int NumberOfDecisionPoints { get; init; }
    public required ComplexityLevel ComplexityLevel { get; init; }
    public required List<ComplexityHotspot> Hotspots { get; init; } = new();
}

/// <summary>
/// Levels of complexity
/// </summary>
public enum ComplexityLevel
{
    Simple,     // 1-10
    Moderate,   // 11-20
    Complex,    // 21-50
    VeryComplex // > 50
}

/// <summary>
/// Hotspot of complexity in code
/// </summary>
public class ComplexityHotspot
{
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string HotspotType { get; init; }
    public required int LocalComplexity { get; init; }
    public required string Description { get; init; }
    public required string Suggestion { get; init; }
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

/// <summary>
/// A code smell detected in the code
/// </summary>
public class CodeSmell
{
    public required string SmellType { get; init; }
    public required string Description { get; init; }
    public required CodeSmellSeverity Severity { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string AffectedCode { get; init; }
    public required string Suggestion { get; init; }
    public required List<RefactoringPattern> RefactoringPatterns { get; init; } = new();
    public required double ImpactScore { get; init; }
}

/// <summary>
/// Severity levels for code smells
/// </summary>
public enum CodeSmellSeverity
{
    Info,
    Minor,
    Major,
    Critical,
    Blocker
}

/// <summary>
/// Refactoring pattern to fix a code smell
/// </summary>
public class RefactoringPattern
{
    public required string PatternName { get; init; }
    public required string Description { get; init; }
    public required List<string> Steps { get; init; } = new();
    public required double Applicability { get; init; }
    public required int EstimatedEffort { get; init; }
}

/// <summary>
/// A refactoring suggestion with before/after examples
/// </summary>
public class RefactoringSuggestion
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required RefactoringType Type { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required CodeExample BeforeAfter { get; init; }
    public required double Confidence { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required List<string> Benefits { get; init; } = new();
    public required List<string> Risks { get; init; } = new();
}

/// <summary>
/// Types of refactoring
/// </summary>
public enum RefactoringType
{
    ExtractMethod,
    ExtractClass,
    ExtractInterface,
    MoveMethod,
    MoveField,
    RenameMethod,
    ReplaceConditionalWithPolymorphism,
    ReplaceMagicNumber,
    IntroduceParameterObject,
    DecomposeMethod,
    SimplifyConditional,
    ExtractStrategy,
    ReplaceTypeCodeWithSubclass
}

/// <summary>
/// Before and after code example
/// </summary>
public class CodeExample
{
    public required string BeforeCode { get; init; }
    public required string AfterCode { get; init; }
    public required string Explanation { get; init; }
    public required List<string> Changes { get; init; } = new();
}

/// <summary>
/// Comprehensive optimization plan for a file
/// </summary>
public class OptimizationPlan
{
    public required string FilePath { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required List<OptimizationAction> Actions { get; init; } = new();
    public required OptimizationPriority OverallPriority { get; init; }
    public required int TotalEstimatedEffortHours { get; init; }
    public required double TotalExpectedBenefit { get; init; }
    public required List<string> Prerequisites { get; init; } = new();
    public required List<string> Risks { get; init; } = new();
    public required List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// An optimization action to perform
/// </summary>
public class OptimizationAction
{
    public required string ActionId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required OptimizationActionType ActionType { get; init; }
    public required int Priority { get; init; }
    public required int EstimatedEffortHours { get; init; }
    public required double ExpectedBenefit { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required List<string> Dependencies { get; init; } = new();
    public required List<RefactoringSuggestion> RefactoringSuggestions { get; init; } = new();
    public required bool IsRecommended { get; init; }
}

/// <summary>
/// Types of optimization actions
/// </summary>
public enum OptimizationActionType
{
    ExtractClass,
    ExtractMethod,
    ExtractInterface,
    SplitFile,
    ReduceComplexity,
    RemoveDuplication,
    ImproveNaming,
    ReduceCoupling,
    IncreaseCohesion,
    RefactorConditional
}

/// <summary>
/// Priority levels for optimization
/// </summary>
public enum OptimizationPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Estimate for optimization effort and impact
/// </summary>
public class OptimizationEstimate
{
    public required int TotalEffortHours { get; init; }
    public required Dictionary<OptimizationActionType, int> EffortByType { get; init; } = new();
    public required double OverallBenefit { get; init; }
    public required Dictionary<string, double> Benefits { get; init; } = new();
    public required List<string> HighImpactActions { get; init; } = new();
    public required List<string> LowEffortHighBenefitActions { get; init; } = new();
    public required RiskAssessment RiskAssessment { get; init; }
    public required List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// Assessment of risks involved in optimization
/// </summary>
public class RiskAssessment
{
    public required RiskLevel OverallRisk { get; init; }
    public required List<RiskFactor> RiskFactors { get; init; } = new();
    public required List<string> MitigationStrategies { get; init; } = new();
}

/// <summary>
/// Risk levels
/// </summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// A risk factor in the optimization
/// </summary>
public class RiskFactor
{
    public required string RiskType { get; init; }
    public required string Description { get; init; }
    public required RiskLevel Severity { get; init; }
    public required string Mitigation { get; init; }
}

/// <summary>
/// Optimization statistics
/// </summary>
public class OptimizationStatistics
{
    public required int FilesNeedingOptimization { get; init; }
    public required int ClassesNeedingSplitting { get; init; }
    public required int MethodsNeedingRefactoring { get; init; }
    public required int GodClassesDetected { get; init; }
    public required int GodMethodsDetected { get; init; }
    public required double AverageComplexityScore { get; init; }
    public required int TotalCodeSmells { get; init; }
    public required Dictionary<CodeSmellSeverity, int> CodeSmellsBySeverity { get; init; } = new();
    public required Dictionary<OptimizationPriority, int> FilesByPriority { get; init; } = new();
}

/// <summary>
/// General optimization recommendation
/// </summary>
public class OptimizationRecommendation
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required RecommendationType Type { get; init; }
    public required OptimizationPriority Priority { get; init; }
    public required List<string> AffectedFiles { get; init; } = new();
    public required int EstimatedEffortHours { get; init; }
    public required double ExpectedBenefit { get; init; }
}

/// <summary>
/// Types of recommendations
/// </summary>
public enum RecommendationType
{
    ProjectStructure,
    CodeOrganization,
    ComplexityReduction,
    MaintainabilityImprovement,
    PerformanceOptimization,
    TestingImprovement
}