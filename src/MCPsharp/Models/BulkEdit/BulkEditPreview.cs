namespace MCPsharp.Models.BulkEdit;

/// <summary>
/// Preview of bulk edit changes without applying them
/// </summary>
public class BulkEditPreview
{
    /// <summary>
    /// List of files that would be modified
    /// </summary>
    public required IReadOnlyList<FileChangePreview> FileChanges { get; init; }

    /// <summary>
    /// Total number of files that would be affected
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Total number of changes across all files
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Estimated impact and risk assessment
    /// </summary>
    public required ImpactAssessment Impact { get; init; }

    /// <summary>
    /// Validation issues found during preview
    /// </summary>
    public required IReadOnlyList<ValidationIssue> ValidationIssues { get; init; }

    /// <summary>
    /// Summary of the preview
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Preview of changes for a single file
/// </summary>
public class FileChangePreview
{
    /// <summary>
    /// Path to the file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// List of changes that would be made
    /// </summary>
    public required IReadOnlyList<FileChange> Changes { get; init; }

    /// <summary>
    /// Number of changes
    /// </summary>
    public required int ChangeCount { get; init; }

    /// <summary>
    /// Risk level for this file
    /// </summary>
    public ValidationSeverity RiskLevel { get; init; } = ValidationSeverity.Warning;

    /// <summary>
    /// Whether the file would be backed up before changes
    /// </summary>
    public required bool WillCreateBackup { get; init; }
}

/// <summary>
/// Impact assessment for bulk changes
/// </summary>
public class ImpactAssessment
{
    /// <summary>
    /// Overall risk level
    /// </summary>
    public ValidationSeverity OverallRisk { get; init; } = ValidationSeverity.Warning;

    /// <summary>
    /// Number of critical files that would be modified
    /// </summary>
    public required int CriticalFilesCount { get; init; }

    /// <summary>
    /// List of potentially risky changes
    /// </summary>
    public required IReadOnlyList<string> RiskFactors { get; init; }

    /// <summary>
    /// Estimated time to apply changes
    /// </summary>
    public required TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Recommendations before applying changes
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Result of previewing bulk changes before applying them
/// </summary>
public class PreviewResult
{
    /// <summary>
    /// Whether the preview was generated successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if preview generation failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Files that would be modified
    /// </summary>
    public required IReadOnlyList<FilePreviewResult> FilePreviews { get; init; } = new List<FilePreviewResult>();

    /// <summary>
    /// Summary of the preview results
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Impact assessment for the changes
    /// </summary>
    public required ImpactEstimate Impact { get; init; }

    /// <summary>
    /// Unique identifier for this preview session
    /// </summary>
    public required string PreviewId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the preview was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Summary of preview results
/// </summary>
public class PreviewSummary
{
    /// <summary>
    /// Total files in the preview
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Files that would be changed
    /// </summary>
    public required int FilesToChange { get; init; }

    /// <summary>
    /// Files that would be skipped
    /// </summary>
    public required int FilesToSkip { get; init; }

    /// <summary>
    /// Total changes across all files
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Number of lines added
    /// </summary>
    public required int LinesAdded { get; init; }

    /// <summary>
    /// Number of lines removed
    /// </summary>
    public required int LinesRemoved { get; init; }

    /// <summary>
    /// Change in file size in bytes
    /// </summary>
    public required long SizeChange { get; init; }
}

/// <summary>
/// Preview of changes for a single file
/// </summary>
public class FilePreviewResult
{
    /// <summary>
    /// Path to the file being previewed
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the preview was generated successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if preview generation failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// List of changes that would be made
    /// </summary>
    public required IReadOnlyList<FileChange> Changes { get; init; } = new List<FileChange>();

    /// <summary>
    /// Total number of changes
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Estimated risk level for this file
    /// </summary>
    public required RiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Lines that would be affected
    /// </summary>
    public required IReadOnlyList<int> AffectedLines { get; init; } = new List<int>();

    /// <summary>
    /// Whether the file is writable
    /// </summary>
    public required bool IsWritable { get; init; }

    /// <summary>
    /// Whether the file is under source control
    /// </summary>
    public required bool IsUnderSourceControl { get; init; }

    /// <summary>
    /// Size of the file in bytes
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// When the preview was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the file would be changed
    /// </summary>
    public required bool WouldChange { get; init; }

    /// <summary>
    /// Number of changes that would be made (alias for TotalChanges)
    /// </summary>
    public int ChangeCount => TotalChanges;

    /// <summary>
    /// Reason why the file would be skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Diff preview of the changes
    /// </summary>
    public string? DiffPreview { get; init; }

    /// <summary>
    /// Planned changes for this file
    /// </summary>
    public IReadOnlyList<FileChange>? PlannedChanges { get; init; }
}

/// <summary>
/// Estimate of the impact and complexity of a bulk operation
/// </summary>
public class ImpactEstimate
{
    /// <summary>
    /// Overall risk level for the operation
    /// </summary>
    public required ChangeRiskLevel OverallRisk { get; init; }

    /// <summary>
    /// List of specific risks identified
    /// </summary>
    public required IReadOnlyList<RiskItem> Risks { get; init; } = Array.Empty<RiskItem>();

    /// <summary>
    /// Complexity estimate for the operation
    /// </summary>
    public required ComplexityEstimate Complexity { get; init; }

    /// <summary>
    /// Recommendations for safe execution
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    // Legacy properties for backward compatibility

    /// <summary>
    /// Overall complexity level (legacy)
    /// </summary>
    public ComplexityLevel ComplexityLevel => OverallRisk switch
    {
        ChangeRiskLevel.Critical => ComplexityLevel.VeryComplex,
        ChangeRiskLevel.High => ComplexityLevel.Complex,
        ChangeRiskLevel.Medium => ComplexityLevel.Moderate,
        _ => ComplexityLevel.Simple
    };

    /// <summary>
    /// Estimated number of files to be modified (legacy)
    /// </summary>
    public int EstimatedFilesAffected => Risks.SelectMany(r => r.AffectedFiles).Distinct().Count();

    /// <summary>
    /// Estimated number of total changes (legacy)
    /// </summary>
    public int EstimatedTotalChanges => Risks.Count;

    /// <summary>
    /// Estimated time to complete the operation (legacy)
    /// </summary>
    public TimeSpan EstimatedDuration => Complexity.EstimatedTime;

    /// <summary>
    /// Risk assessment (legacy)
    /// </summary>
    public RiskLevel RiskLevel => OverallRisk switch
    {
        ChangeRiskLevel.Critical => RiskLevel.Critical,
        ChangeRiskLevel.High => RiskLevel.High,
        ChangeRiskLevel.Medium => RiskLevel.Medium,
        _ => RiskLevel.Low
    };

    /// <summary>
    /// Files that might be affected but are uncertain (legacy)
    /// </summary>
    public IReadOnlyList<string> UncertainFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Potential breaking changes (legacy)
    /// </summary>
    public IReadOnlyList<string> BreakingChanges { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Dependencies that might be affected (legacy)
    /// </summary>
    public IReadOnlyList<string> AffectedDependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence level in the estimate (legacy)
    /// </summary>
    public double ConfidenceLevel { get; init; } = 0.8;
}
