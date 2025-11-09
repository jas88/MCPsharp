namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Duplicate hotspots analysis result
/// </summary>
public class DuplicateHotspotsResult
{
    /// <summary>
    /// Files with highest duplication rates
    /// </summary>
    public required IReadOnlyList<FileHotspot> FileHotspots { get; init; }

    /// <summary>
    /// Classes with highest duplication rates
    /// </summary>
    public required IReadOnlyList<ClassHotspot> ClassHotspots { get; init; }

    /// <summary>
    /// Methods with highest duplication rates
    /// </summary>
    public required IReadOnlyList<MethodHotspot> MethodHotspots { get; init; }

    /// <summary>
    /// Directories with highest duplication rates
    /// </summary>
    public required IReadOnlyList<DirectoryHotspot> DirectoryHotspots { get; init; }

    /// <summary>
    /// Trends in duplication over time (if historical data available)
    /// </summary>
    public IReadOnlyList<DuplicationTrend>? Trends { get; init; }
}

/// <summary>
/// File duplication hotspot
/// </summary>
public class FileHotspot
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Duplication percentage
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Class duplication hotspot
/// </summary>
public class ClassHotspot
{
    /// <summary>
    /// Class name
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Method duplication hotspot
/// </summary>
public class MethodHotspot
{
    /// <summary>
    /// Method name
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Containing class
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Directory duplication hotspot
/// </summary>
public class DirectoryHotspot
{
    /// <summary>
    /// Directory path
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Duplication score
    /// </summary>
    public required double DuplicationScore { get; init; }

    /// <summary>
    /// Number of files with duplicates
    /// </summary>
    public required int FilesWithDuplicates { get; init; }

    /// <summary>
    /// Total duplicate lines
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Risk level
    /// </summary>
    public required HotspotRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Duplication trend over time
/// </summary>
public class DuplicationTrend
{
    /// <summary>
    /// Date of the measurement
    /// </summary>
    public required DateTime Date { get; init; }

    /// <summary>
    /// Duplication percentage
    /// </summary>
    public required double DuplicationPercentage { get; init; }

    /// <summary>
    /// Total duplicate lines
    /// </summary>
    public required int TotalDuplicateLines { get; init; }

    /// <summary>
    /// Number of duplicate groups
    /// </summary>
    public required int DuplicateGroupCount { get; init; }
}
