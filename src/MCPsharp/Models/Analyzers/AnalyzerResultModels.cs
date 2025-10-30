using System.Collections.Generic;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Result of an analyzer execution
/// </summary>
public record AnalyzerResult
{
    /// <summary>
    /// Whether the analysis was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Identifier of the analyzer that produced this result
    /// </summary>
    public string AnalyzerId { get; init; } = string.Empty;

    /// <summary>
    /// Collection of findings discovered during analysis
    /// </summary>
    public List<Finding> Findings { get; init; } = new();

    /// <summary>
    /// Error message if analysis failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp when the analysis was completed
    /// </summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a single finding discovered by an analyzer
/// </summary>
public record Finding
{
    /// <summary>
    /// Severity level of the finding
    /// </summary>
    public FindingSeverity Severity { get; init; } = FindingSeverity.Info;

    /// <summary>
    /// Descriptive message explaining the finding
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// File path where the finding was located (optional)
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Line number where the finding was located (optional)
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Column number where the finding was located (optional)
    /// </summary>
    public int? ColumnNumber { get; init; }

    /// <summary>
    /// Unique identifier for this finding
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the finding was discovered
    /// </summary>
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Severity levels for analyzer findings
/// </summary>
public enum FindingSeverity
{
    /// <summary>
    /// Informational finding that doesn't indicate a problem
    /// </summary>
    Info,

    /// <summary>
    /// Minor issue that should be addressed but isn't critical
    /// </summary>
    Warning,

    /// <summary>
    /// Significant issue that should be fixed
    /// </summary>
    Error,

    /// <summary>
    /// Critical issue that requires immediate attention
    /// </summary>
    Critical
}

/// <summary>
/// Configuration options for analyzer execution
/// </summary>
public record AnalyzerOptions
{
    /// <summary>
    /// Whether to enable semantic analysis
    /// </summary>
    public bool EnableSemanticAnalysis { get; init; } = true;

    /// <summary>
    /// Whether to enable performance analysis
    /// </summary>
    public bool EnablePerformanceAnalysis { get; init; } = true;

    /// <summary>
    /// Whether to enable security analysis
    /// </summary>
    public bool EnableSecurityAnalysis { get; init; } = false;

    /// <summary>
    /// Whether to enable style analysis
    /// </summary>
    public bool EnableStyleAnalysis { get; init; } = false;

    /// <summary>
    /// Minimum severity level to report
    /// </summary>
    public FindingSeverity MinimumSeverity { get; init; } = FindingSeverity.Info;

    /// <summary>
    /// Maximum number of findings to return
    /// </summary>
    public int MaxFindings { get; init; } = 1000;

    /// <summary>
    /// Whether to include context information in findings
    /// </summary>
    public bool IncludeContext { get; init; } = true;

    /// <summary>
    /// Whether to exclude generated code from analysis
    /// </summary>
    public bool ExcludeGeneratedCode { get; init; } = true;

    /// <summary>
    /// Custom analyzer-specific properties
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; init; } = new();
}