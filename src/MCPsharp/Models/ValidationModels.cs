namespace MCPsharp.Models;

/// <summary>
/// Validation result for bulk edit operations
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// List of validation issues found
    /// </summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Overall validation severity
    /// </summary>
    public ValidationSeverity OverallSeverity { get; init; } = ValidationSeverity.None;

    /// <summary>
    /// Validation summary message
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// A validation issue found during analysis
/// </summary>
public class ValidationIssue
{
    /// <summary>
    /// Type of validation issue
    /// </summary>
    public required ValidationIssueType Type { get; init; }

    /// <summary>
    /// Severity of the issue
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Description of the issue
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// File path where the issue was found
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Line number where the issue was found
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Column number where the issue was found
    /// </summary>
    public int? ColumnNumber { get; init; }

    /// <summary>
    /// Code snippet that caused the issue
    /// </summary>
    public string? CodeSnippet { get; init; }

    /// <summary>
    /// Suggested fix for the issue
    /// </summary>
    public string? SuggestedFix { get; init; }

    /// <summary>
    /// Whether this issue is blocking
    /// </summary>
    public bool IsBlocking { get; init; }

    /// <summary>
    /// Additional context about the issue
    /// </summary>
    public Dictionary<string, object>? Context { get; init; }
}

/// <summary>
/// Types of validation issues
/// </summary>
public enum ValidationIssueType
{
    /// <summary>
    /// Syntax error in code
    /// </summary>
    SyntaxError,

    /// <summary>
    /// Compilation error
    /// </summary>
    CompilationError,

    /// <summary>
    /// Reference to undefined symbol
    /// </summary>
    UndefinedReference,

    /// <summary>
    /// Type mismatch
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// Invalid file path
    /// </summary>
    InvalidPath,

    /// <summary>
    /// File access issue
    /// </summary>
    FileAccessIssue,

    /// <summary>
    /// Pattern matching error
    /// </summary>
    PatternError,

    /// <summary>
    /// Regex compilation error
    /// </summary>
    RegexError,

    /// <summary>
    /// Logic error in operation
    /// </summary>
    LogicError,

    /// <summary>
    /// Performance concern
    /// </summary>
    PerformanceIssue,

    /// <summary>
    /// Security concern
    /// </summary>
    SecurityIssue,

    /// <summary>
    /// Best practice violation
    /// </summary>
    BestPracticeViolation,

    /// <summary>
    /// Style guideline violation
    /// </summary>
    StyleViolation,

    /// <summary>
    /// Potential data loss
    /// </summary>
    DataLossRisk,

    /// <summary>
    /// Dependency issue
    /// </summary>
    DependencyIssue,

    /// <summary>
    /// Configuration issue
    /// </summary>
    ConfigurationIssue,

    /// <summary>
    /// Other type of issue
    /// </summary>
    Other
}

/// <summary>
/// Severity levels for validation issues
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// No issues
    /// </summary>
    None,

    /// <summary>
    /// Informational message
    /// </summary>
    Info,

    /// <summary>
    /// Warning that should be reviewed
    /// </summary>
    Warning,

    /// <summary>
    /// Error that should be fixed
    /// </summary>
    Error,

    /// <summary>
    /// Critical error that blocks operation
    /// </summary>
    Critical
}

/// <summary>
/// Represents a single file change
/// </summary>
public class FileChange
{
    /// <summary>
    /// Type of change
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// Line number where the change starts
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Column number where the change starts
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Line number where the change ends
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Column number where the change ends
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// Original text that was changed
    /// </summary>
    public string? OriginalText { get; init; }

    /// <summary>
    /// New text that replaces the original
    /// </summary>
    public string? NewText { get; init; }

    /// <summary>
    /// Description of what changed
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Context around the change
    /// </summary>
    public string? Context { get; init; }
}

/// <summary>
/// Types of file changes
/// </summary>
public enum FileChangeType
{
    /// <summary>
    /// Text was inserted
    /// </summary>
    Insert,

    /// <summary>
    /// Text was deleted
    /// </summary>
    Delete,

    /// <summary>
    /// Text was replaced
    /// </summary>
    Replace,

    /// <summary>
    /// Line was modified
    /// </summary>
    ModifyLine,

    /// <summary>
    /// Multiple lines were modified
    /// </summary>
    ModifyLines,

    /// <summary>
    /// File was moved/renamed
    /// </summary>
    Move,

    /// <summary>
    /// File was copied
    /// </summary>
    Copy,

    /// <summary>
    /// File was deleted
    /// </summary>
    DeleteFile,

    /// <summary>
    /// File was created
    /// </summary>
    CreateFile
}