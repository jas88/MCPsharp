namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Severity levels for analyzer issues
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Confidence levels for analyzer suggestions
/// </summary>
public enum Confidence
{
    Low,
    Medium,
    High,
    VeryHigh
}

/// <summary>
/// Category of analyzer rule
/// </summary>
public enum RuleCategory
{
    CodeQuality,
    Performance,
    Security,
    Maintainability,
    Reliability,
    Style,
    Migration,
    Design
}