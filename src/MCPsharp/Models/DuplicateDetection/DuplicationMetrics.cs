namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Duplication metrics and statistics
/// </summary>
public class DuplicationMetrics
{
    /// <summary>
    /// Total number of duplicate groups found
    /// </summary>
    public required int TotalDuplicateGroups { get; init; }

    /// <summary>
    /// Number of exact duplicate groups
    /// </summary>
    public required int ExactDuplicateGroups { get; init; }

    /// <summary>
    /// Number of near-miss duplicate groups
    /// </summary>
    public required int NearMissDuplicateGroups { get; init; }

    /// <summary>
    /// Total lines of duplicated code
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Percentage of code that is duplicated
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Number of files containing duplicates
    /// </summary>
    public required int FilesWithDuplicates { get; init; }

    /// <summary>
    /// Duplication by type
    /// </summary>
    public required IReadOnlyDictionary<DuplicateType, int> DuplicationByType { get; init; }

    /// <summary>
    /// Duplication by file
    /// </summary>
    public required IReadOnlyDictionary<string, FileDuplicationMetrics> DuplicationByFile { get; init; }

    /// <summary>
    /// Duplication by similarity range
    /// </summary>
    public required IReadOnlyDictionary<string, int> DuplicationBySimilarity { get; init; }

    /// <summary>
    /// Average similarity score across all duplicates
    /// </summary>
    public required double AverageSimilarity { get; init; }

    /// <summary>
    /// Highest similarity score found
    /// </summary>
    public required double MaxSimilarity { get; init; }

    /// <summary>
    /// Lowest similarity score found
    /// </summary>
    public required double MinSimilarity { get; init; }

    /// <summary>
    /// Estimated cost of duplication in maintenance hours
    /// </summary>
    public required double EstimatedMaintenanceCost { get; init; }

    /// <summary>
    /// Estimated savings from refactoring
    /// </summary>
    public required double EstimatedRefactoringSavings { get; init; }
}

/// <summary>
/// File-specific duplication metrics
/// </summary>
public class FileDuplicationMetrics
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Number of duplicate groups in this file
    /// </summary>
    public required int DuplicateGroups { get; init; }

    /// <summary>
    /// Number of lines duplicated in this file
    /// </summary>
    public required int DuplicateLines { get; init; }

    /// <summary>
    /// Percentage of file that is duplicated
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Types of duplication found in this file
    /// </summary>
    public required IReadOnlyList<DuplicateType> DuplicationTypes { get; init; }

    /// <summary>
    /// Average complexity of duplicated code in this file
    /// </summary>
    public required double AverageComplexity { get; init; }
}
