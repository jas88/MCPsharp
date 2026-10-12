namespace MCPsharp.Models.Analyzers;

/// <summary>
/// What happened to one diagnostic when code fixes were applied
/// </summary>
public enum CodeFixOutcomeStatus
{
    /// <summary>The fix was applied to the document</summary>
    Applied,

    /// <summary>The diagnostic was no longer reported when its turn came (fixed or invalidated by an earlier fix)</summary>
    Skipped,

    /// <summary>No provider produced a usable change for the diagnostic, or computing the fix failed</summary>
    Failed
}

/// <summary>
/// A diagnostic to fix, identified by rule and 1-based start location in the document as it was
/// when the fix run started
/// </summary>
public sealed record CodeFixTarget(string RuleId, int Line, int Column);

/// <summary>
/// Result of fixing one diagnostic
/// </summary>
public sealed record CodeFixOutcome
{
    /// <summary>Diagnostic ID</summary>
    public required string RuleId { get; init; }

    /// <summary>1-based line of the diagnostic in the original document</summary>
    public required int Line { get; init; }

    /// <summary>1-based column of the diagnostic in the original document</summary>
    public required int Column { get; init; }

    /// <summary>Diagnostic message, when the diagnostic was found</summary>
    public string? Message { get; init; }

    /// <summary>Whether the fix was applied, skipped or failed</summary>
    public required CodeFixOutcomeStatus Status { get; init; }

    /// <summary>Title of the code action that was (or would be) applied</summary>
    public string? FixTitle { get; init; }

    /// <summary>Name of the code fix provider that produced the change</summary>
    public string? Provider { get; init; }

    /// <summary>Why the fix was skipped or failed</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// A document whose text was changed by a fix run
/// </summary>
public sealed record ChangedDocumentText(string FilePath, string OriginalText, string NewText, string Diff);

/// <summary>
/// Result of applying code fixes to a document in memory
/// </summary>
public sealed record CodeFixRunResult
{
    /// <summary>Per-diagnostic outcomes, in document order</summary>
    public required IReadOnlyList<CodeFixOutcome> Outcomes { get; init; }

    /// <summary>Documents whose text differs from the starting state</summary>
    public required IReadOnlyList<ChangedDocumentText> ChangedDocuments { get; init; }

    /// <summary>Number of applied fixes</summary>
    public int AppliedCount => Outcomes.Count(o => o.Status == CodeFixOutcomeStatus.Applied);

    /// <summary>Number of skipped fixes</summary>
    public int SkippedCount => Outcomes.Count(o => o.Status == CodeFixOutcomeStatus.Skipped);

    /// <summary>Number of failed fixes</summary>
    public int FailedCount => Outcomes.Count(o => o.Status == CodeFixOutcomeStatus.Failed);
}
