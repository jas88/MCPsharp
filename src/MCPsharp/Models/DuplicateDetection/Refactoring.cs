using MCPsharp.Models;

namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Refactoring suggestion for duplicate code
/// </summary>
public class RefactoringSuggestion
{
    /// <summary>
    /// Unique identifier for this suggestion
    /// </summary>
    public required string SuggestionId { get; init; }

    /// <summary>
    /// Type of refactoring suggested
    /// </summary>
    public required RefactoringType RefactoringType { get; init; }

    /// <summary>
    /// Title of the refactoring suggestion
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed description of the refactoring
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Duplicate groups this refactoring would address
    /// </summary>
    public required IReadOnlyList<string> DuplicateGroupIds { get; init; }

    /// <summary>
    /// Priority level of this refactoring
    /// </summary>
    public required RefactoringPriority Priority { get; init; }

    /// <summary>
    /// Estimated effort required (in hours)
    /// </summary>
    public required double EstimatedEffort { get; init; }

    /// <summary>
    /// Estimated benefit from this refactoring
    /// </summary>
    public required double EstimatedBenefit { get; init; }

    /// <summary>
    /// Risk level of this refactoring
    /// </summary>
    public required RefactoringRisk Risk { get; init; }

    /// <summary>
    /// Whether this refactoring would introduce breaking changes
    /// </summary>
    public required bool IsBreakingChange { get; init; }

    /// <summary>
    /// Steps to implement this refactoring
    /// </summary>
    public required IReadOnlyList<RefactoringStep> ImplementationSteps { get; init; }

    /// <summary>
    /// Prerequisites for this refactoring
    /// </summary>
    public required IReadOnlyList<string> Prerequisites { get; init; }

    /// <summary>
    /// Potential side effects
    /// </summary>
    public required IReadOnlyList<string> SideEffects { get; init; }

    /// <summary>
    /// Validation results for this refactoring
    /// </summary>
    public RefactoringValidationResult? ValidationResult { get; init; }

    /// <summary>
    /// Metadata about this suggestion
    /// </summary>
    public required RefactoringMetadata Metadata { get; init; }
}

/// <summary>
/// Step in a refactoring implementation
/// </summary>
public class RefactoringStep
{
    /// <summary>
    /// Step description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Type of operation
    /// </summary>
    public required RefactoringOperationType OperationType { get; init; }

    /// <summary>
    /// Target file paths
    /// </summary>
    public required IReadOnlyList<string> TargetFiles { get; init; }

    /// <summary>
    /// Estimated time for this step (in minutes)
    /// </summary>
    public required int EstimatedMinutes { get; init; }

    /// <summary>
    /// Whether this step is optional
    /// </summary>
    public required bool IsOptional { get; init; }

    /// <summary>
    /// Dependencies on other steps
    /// </summary>
    public required IReadOnlyList<int> Dependencies { get; init; }
}

/// <summary>
/// Result of refactoring validation
/// </summary>
public class RefactoringValidationResult
{
    /// <summary>
    /// Whether the refactoring is valid
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Validation issues found
    /// </summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Impact on dependencies
    /// </summary>
    public required IReadOnlyList<DependencyImpact> DependencyImpacts { get; init; }

    /// <summary>
    /// Overall risk assessment
    /// </summary>
    public required RefactoringRisk OverallRisk { get; init; }

    /// <summary>
    /// Recommendations
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Impact on dependencies
/// </summary>
public class DependencyImpact
{
    /// <summary>
    /// Dependent file
    /// </summary>
    public required string DependentFile { get; init; }

    /// <summary>
    /// Impact type
    /// </summary>
    public required DependencyImpactType ImpactType { get; init; }

    /// <summary>
    /// Impact description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Whether this is a breaking change
    /// </summary>
    public required bool IsBreakingChange { get; init; }
}

/// <summary>
/// Metadata for refactoring suggestions
/// </summary>
public class RefactoringMetadata
{
    /// <summary>
    /// When this suggestion was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>
    /// Algorithm used to generate the suggestion
    /// </summary>
    public required string GenerationAlgorithm { get; init; }

    /// <summary>
    /// Confidence score for this suggestion
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public required Dictionary<string, object> AdditionalMetadata { get; init; }
}
