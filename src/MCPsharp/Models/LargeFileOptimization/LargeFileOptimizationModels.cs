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

    /// <summary>
    /// Classes in the file with a god-class severity above <c>None</c>
    /// </summary>
    public int GodClassCount { get; init; }

    /// <summary>
    /// Methods in the file with a god-method severity above <c>None</c>
    /// </summary>
    public int GodMethodCount { get; init; }
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