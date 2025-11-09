namespace MCPsharp.Models.BulkEdit;

/// <summary>
/// Risk item for impact assessment
/// </summary>
public class RiskItem
{
    /// <summary>
    /// Type of risk
    /// </summary>
    public required RiskType Type { get; init; }

    /// <summary>
    /// Description of the risk
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Files affected by this risk
    /// </summary>
    public required IReadOnlyList<string> AffectedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Severity of the risk
    /// </summary>
    public required RiskSeverity Severity { get; init; }

    /// <summary>
    /// Mitigation strategy
    /// </summary>
    public string? Mitigation { get; init; }
}

/// <summary>
/// Types of risks
/// </summary>
public enum RiskType
{
    /// <summary>
    /// Compilation breaking changes
    /// </summary>
    Compilation,

    /// <summary>
    /// Dependency issues
    /// </summary>
    Dependencies,

    /// <summary>
    /// Performance impact
    /// </summary>
    Performance,

    /// <summary>
    /// File access issues
    /// </summary>
    FileAccessIssue,

    /// <summary>
    /// Other types of risks
    /// </summary>
    Other
}

/// <summary>
/// Risk severity levels
/// </summary>
public enum RiskSeverity
{
    /// <summary>
    /// Low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}

/// <summary>
/// Change risk levels (different from RiskLevel for compatibility)
/// </summary>
public enum ChangeRiskLevel
{
    /// <summary>
    /// Very low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}

/// <summary>
/// Complexity estimate for operations
/// </summary>
public class ComplexityEstimate
{
    /// <summary>
    /// Complexity score (0-100)
    /// </summary>
    public required int Score { get; init; }

    /// <summary>
    /// Complexity level description
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// Factors contributing to complexity
    /// </summary>
    public required IReadOnlyList<string> Factors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Estimated time to complete
    /// </summary>
    public required TimeSpan EstimatedTime { get; init; }
}

/// <summary>
/// Complexity levels for operations
/// </summary>
public enum ComplexityLevel
{
    /// <summary>
    /// Simple, low-risk operation
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate complexity
    /// </summary>
    Moderate,

    /// <summary>
    /// Complex operation requiring care
    /// </summary>
    Complex,

    /// <summary>
    /// Very complex, high-risk operation
    /// </summary>
    VeryComplex
}

/// <summary>
/// Risk levels for operations
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Very low risk
    /// </summary>
    Low,

    /// <summary>
    /// Moderate risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk operation
    /// </summary>
    High,

    /// <summary>
    /// Very high risk, potentially breaking
    /// </summary>
    Critical
}
