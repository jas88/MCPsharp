namespace MCPsharp.Models.LargeFileOptimization;

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
