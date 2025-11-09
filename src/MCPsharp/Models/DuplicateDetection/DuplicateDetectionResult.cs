using Microsoft.Extensions.Logging;

namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Result of duplicate code detection
/// </summary>
public class DuplicateDetectionResult
{
    /// <summary>
    /// List of duplicate code groups found
    /// </summary>
    public required IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; }

    /// <summary>
    /// Metrics about the duplication analysis
    /// </summary>
    public required DuplicationMetrics Metrics { get; init; }

    /// <summary>
    /// Refactoring suggestions for the duplicates
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion>? RefactoringSuggestions { get; init; }

    /// <summary>
    /// Hotspots analysis
    /// </summary>
    public DuplicateHotspotsResult? Hotspots { get; init; }

    /// <summary>
    /// Analysis duration
    /// </summary>
    public required TimeSpan AnalysisDuration { get; init; }

    /// <summary>
    /// Number of files analyzed
    /// </summary>
    public required int FilesAnalyzed { get; init; }

    /// <summary>
    /// Any warnings or issues during analysis
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Whether the analysis was completed successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if analysis failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}
