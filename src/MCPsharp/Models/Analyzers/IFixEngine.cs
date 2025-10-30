namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Engine for applying analyzer fixes
/// </summary>
public interface IFixEngine
{
    /// <summary>
    /// Preview fixes for issues
    /// </summary>
    Task<FixPreviewResult> PreviewFixesAsync(ImmutableArray<AnalyzerIssue> issues, ImmutableArray<AnalyzerFix> fixes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply fixes to files
    /// </summary>
    Task<FixApplicationResult> ApplyFixesAsync(ImmutableArray<AnalyzerIssue> issues, ImmutableArray<AnalyzerFix> fixes, FixApplicationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect conflicts between fixes
    /// </summary>
    Task<ConflictDetectionResult> DetectConflictsAsync(ImmutableArray<AnalyzerFix> fixes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve conflicts automatically
    /// </summary>
    Task<ConflictResolutionResult> ResolveConflictsAsync(ImmutableArray<FixConflict> conflicts, ConflictResolutionStrategy strategy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback applied fixes
    /// </summary>
    Task<RollbackResult> RollbackFixesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate applied fixes
    /// </summary>
    Task<ValidationResult> ValidateFixesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get fix history
    /// </summary>
    Task<ImmutableArray<FixSession>> GetFixHistoryAsync(int maxSessions = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get fix statistics
    /// </summary>
    Task<FixStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of fix preview
/// </summary>
public record FixPreviewResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<FixPreview> Previews { get; init; } = ImmutableArray<FixPreview>.Empty;
    public ImmutableArray<FixConflict> Conflicts { get; init; } = ImmutableArray<FixConflict>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Preview of a single fix
/// </summary>
public record FixPreview
{
    public string FixId { get; init; } = string.Empty;
    public string IssueId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string OriginalContent { get; init; } = string.Empty;
    public string ModifiedContent { get; init; } = string.Empty;
    public ImmutableArray<TextEdit> Edits { get; init; } = ImmutableArray<TextEdit>.Empty;
    public string Description { get; init; } = string.Empty;
    public Confidence Confidence { get; init; }
}

/// <summary>
/// Result of applying fixes
/// </summary>
public record FixApplicationResult
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<FixResult> AppliedFixes { get; init; } = ImmutableArray<FixResult>.Empty;
    public ImmutableArray<string> ModifiedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<FixConflict> ResolvedConflicts { get; init; } = ImmutableArray<FixConflict>.Empty;
    public ImmutableArray<string> FailedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Options for applying fixes
/// </summary>
public record FixApplicationOptions
{
    public bool CreateBackup { get; init; } = true;
    public bool ResolveConflicts { get; init; } = true;
    public ConflictResolutionStrategy ConflictStrategy { get; init; } = ConflictResolutionStrategy.PreferNewer;
    public bool ValidateAfterApply { get; init; } = true;
    public bool StopOnFirstError { get; init; } = false;
    public int MaxParallelFixes { get; init; } = Environment.ProcessorCount;
    public string? BackupDirectory { get; init; }
    public ImmutableArray<string> ExcludeFiles { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Conflict between fixes
/// </summary>
public record FixConflict
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string FilePath { get; init; } = string.Empty;
    public ImmutableArray<string> ConflictingFixIds { get; init; } = ImmutableArray<string>.Empty;
    public ConflictType ConflictType { get; init; }
    public string Description { get; init; } = string.Empty;
    public ImmutableArray<TextEdit> ConflictingEdits { get; init; } = ImmutableArray<TextEdit>.Empty;
    public ConflictResolution? SuggestedResolution { get; init; }
}

/// <summary>
/// Type of conflict
/// </summary>
public enum ConflictType
{
    OverlappingEdits,
    ConflictingChanges,
    DependencyViolation,
    FileAccessConflict,
    SemanticConflict
}

/// <summary>
/// Strategy for resolving conflicts
/// </summary>
public enum ConflictResolutionStrategy
{
    PreferOlder,
    PreferNewer,
    PreferConfidence,
    PreferSeverity,
    Manual,
    SkipAll,
    Abort
}

/// <summary>
/// Resolution for a conflict
/// </summary>
public record ConflictResolution
{
    public string ConflictId { get; init; } = string.Empty;
    public ConflictResolutionStrategy Strategy { get; init; }
    public string? PreferredFixId { get; init; }
    public ImmutableArray<TextEdit> ResolvedEdits { get; init; } = ImmutableArray<TextEdit>.Empty;
    public string? Rationale { get; init; }
}

/// <summary>
/// Result of conflict detection
/// </summary>
public record ConflictDetectionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<FixConflict> Conflicts { get; init; } = ImmutableArray<FixConflict>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Result of conflict resolution
/// </summary>
public record ConflictResolutionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<ConflictResolution> Resolutions { get; init; } = ImmutableArray<ConflictResolution>.Empty;
    public ImmutableArray<FixConflict> UnresolvedConflicts { get; init; } = ImmutableArray<FixConflict>.Empty;
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
    public DateTime ResolvedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Result of rolling back fixes
/// </summary>
public record RollbackResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public ImmutableArray<string> RestoredFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> FailedFiles { get; init; } = ImmutableArray<string>.Empty;
    public DateTime RolledBackAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of validating fixes
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public ImmutableArray<ValidationError> Errors { get; init; } = ImmutableArray<ValidationError>.Empty;
    public ImmutableArray<ValidationWarning> Warnings { get; init; } = ImmutableArray<ValidationWarning>.Empty;
    public DateTime ValidatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Validation error
/// </summary>
public record ValidationError
{
    public string FilePath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FixId { get; init; } = string.Empty;
    public ValidationSeverity Severity { get; init; }
}

/// <summary>
/// Validation warning
/// </summary>
public record ValidationWarning
{
    public string FilePath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FixId { get; init; } = string.Empty;
}

/// <summary>
/// Severity of validation issue
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// A fix session
/// </summary>
public record FixSession
{
    public string SessionId { get; init; } = string.Empty;
    public string AnalyzerId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; }
    public ImmutableArray<string> ModifiedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<FixResult> AppliedFixes { get; init; } = ImmutableArray<FixResult>.Empty;
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Fix statistics
/// </summary>
public record FixStatistics
{
    public int TotalSessions { get; init; }
    public int SuccessfulSessions { get; init; }
    public int FailedSessions { get; init; }
    public int TotalFixes { get; init; }
    public int SuccessfulFixes { get; init; }
    public int FailedFixes { get; init; }
    public int ConflictsDetected { get; init; }
    public int ConflictsResolved { get; init; }
    public ImmutableArray<string> MostFixedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> MostActiveAnalyzers { get; init; } = ImmutableArray<string>.Empty;
    public DateTime PeriodStart { get; init; } = DateTime.UtcNow;
    public DateTime PeriodEnd { get; init; } = DateTime.UtcNow;
}