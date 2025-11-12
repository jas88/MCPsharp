namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Impact assessment for duplication
/// </summary>
public class DuplicationImpact
{
    /// <summary>
    /// Maintenance impact score (0.0-1.0)
    /// </summary>
    public required double MaintenanceImpact { get; init; }

    /// <summary>
    /// Readability impact score (0.0-1.0)
    /// </summary>
    public required double ReadabilityImpact { get; init; }

    /// <summary>
    /// Performance impact score (0.0-1.0)
    /// </summary>
    public required double PerformanceImpact { get; init; }

    /// <summary>
    /// Testability impact score (0.0-1.0)
    /// </summary>
    public required double TestabilityImpact { get; init; }

    /// <summary>
    /// Overall impact score (0.0-1.0)
    /// </summary>
    public required double OverallImpact { get; init; }

    /// <summary>
    /// Impact level
    /// </summary>
    public required ImpactLevel ImpactLevel { get; init; }
}

/// <summary>
/// Complexity metrics for code blocks
/// </summary>
public class ComplexityMetrics
{
    /// <summary>
    /// Cyclomatic complexity
    /// </summary>
    public required int CyclomaticComplexity { get; init; }

    /// <summary>
    /// Cognitive complexity
    /// </summary>
    public required int CognitiveComplexity { get; init; }

    /// <summary>
    /// Lines of code
    /// </summary>
    public required int LinesOfCode { get; init; }

    /// <summary>
    /// Logical lines of code
    /// </summary>
    public required int LogicalLinesOfCode { get; init; }

    /// <summary>
    /// Number of parameters
    /// </summary>
    public required int ParameterCount { get; init; }

    /// <summary>
    /// Nesting depth
    /// </summary>
    public required int NestingDepth { get; init; }

    /// <summary>
    /// Overall complexity score
    /// </summary>
    public required double OverallScore { get; init; }
}

/// <summary>
/// Metadata for duplicate groups
/// </summary>
public class DuplicateMetadata
{
    /// <summary>
    /// When this duplication was first detected
    /// </summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>
    /// Detection algorithm used
    /// </summary>
    public required string DetectionAlgorithm { get; init; }

    /// <summary>
    /// Configuration used for detection
    /// </summary>
    public required string DetectionConfiguration { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public required Dictionary<string, object> AdditionalMetadata { get; init; }
}
