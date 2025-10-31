namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Detail levels for tool responses
/// </summary>
public enum DetailLevel
{
    Basic,      // Essential information only
    Standard,   // Commonly needed information
    Detailed,   // Comprehensive information
    Verbose     // Everything including debug info
}

/// <summary>
/// Flags for additional data to include in responses
/// </summary>
[Flags]
public enum IncludeFlags
{
    Default = 0,
    Metadata = 1 << 0,
    Dependencies = 1 << 1,
    Metrics = 1 << 2,
    History = 1 << 3,
    Suggestions = 1 << 4,
    Preview = 1 << 5,
    NewContent = 1 << 6,
    All = ~0
}

/// <summary>
/// File types identified by the system
/// </summary>
public enum FileType
{
    File,
    Directory,
    Symlink
}

/// <summary>
/// File categories for organization
/// </summary>
public enum FileCategory
{
    SourceCode,
    Configuration,
    Documentation,
    Test,
    Build,
    Data,
    Binary,
    Archive,
    Other
}

/// <summary>
/// File content operations
/// </summary>
public enum FileContentOperation
{
    Read,
    Write,
    Append,
    PartialRead
}

/// <summary>
/// File operation types
/// </summary>
public enum FileOperationType
{
    Edit,
    Validate,
    Copy,
    Move,
    Delete,
    Create
}

/// <summary>
/// Validation types
/// </summary>
public enum ValidationType
{
    Path,
    Existence,
    Size,
    Encoding,
    Readable,
    Permissions
}

/// <summary>
/// Validation severity levels
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Filter types for queries
/// </summary>
public enum FilterType
{
    Pattern,
    Extension,
    Size,
    Date,
    Category,
    Language,
    Content
}

/// <summary>
/// Content transformation types
/// </summary>
public enum TransformationType
{
    Trim,
    NormalizeLineEndings,
    RemoveBlankLines,
    SortLines,
    RemoveComments,
    Format
}

/// <summary>
/// Universal options for all tool requests
/// </summary>
public class ToolOptions
{
    /// <summary>
    /// Requested detail level
    /// </summary>
    public DetailLevel Detail { get; set; } = DetailLevel.Standard;

    /// <summary>
    /// Additional data to include
    /// </summary>
    public IncludeFlags Include { get; set; } = IncludeFlags.Default;

    /// <summary>
    /// Filters to apply
    /// </summary>
    public List<Filter> Filters { get; set; } = new();

    /// <summary>
    /// Sort specification
    /// </summary>
    public SortSpecification? Sort { get; set; }

    /// <summary>
    /// Pagination specification
    /// </summary>
    public PaginationSpec? Pagination { get; set; }
}

/// <summary>
/// Filter specification
/// </summary>
public class Filter
{
    public FilterType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Operator { get; set; } // eq, ne, gt, lt, contains, regex
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Sort specification
/// </summary>
public class SortSpecification
{
    public string Field { get; set; } = string.Empty;
    public bool Ascending { get; set; } = true;
}

/// <summary>
/// Pagination specification
/// </summary>
public class PaginationSpec
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? ContinuationToken { get; set; }
}

/// <summary>
/// Content transformation
/// </summary>
public class ContentTransformation
{
    public TransformationType Type { get; set; }
    public string? Language { get; set; } // For comment removal, etc.
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Base response metadata
/// </summary>
public class ResponseMetadata
{
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ProcessingTime { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public bool HasMore { get; set; }
    public string? ContinuationToken { get; set; }
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Validation result
/// </summary>
public class ValidationResult
{
    public ValidationType Type { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
}

/// <summary>
/// File info request
/// </summary>
public class FileInfoRequest
{
    public required string Path { get; init; }
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// File info response
/// </summary>
public class FileInfoResponse
{
    public required string Path { get; set; }
    public string? RelativePath { get; set; }
    public FileType Type { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime Created { get; set; }
    public string Extension { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public string? Encoding { get; set; }
    public int LineCount { get; set; }
    public string? Language { get; set; }
    public FileCategory FileCategory { get; set; }
    public string? ContentHash { get; set; }
    public string? ContentPreview { get; set; }
    public List<string>? Dependencies { get; set; }
    public List<FileInfoResponse>? Children { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// File content request options
/// </summary>
public class FileContentOptions : ToolOptions
{
    /// <summary>
    /// Create directories if they don't exist (for write operations)
    /// </summary>
    public bool CreateDirectories { get; set; } = true;

    /// <summary>
    /// Overwrite existing files
    /// </summary>
    public bool Overwrite { get; set; } = false;

    /// <summary>
    /// Content transformations to apply
    /// </summary>
    public List<ContentTransformation>? Transformations { get; set; }

    /// <summary>
    /// Start line for partial read
    /// </summary>
    public int? StartLine { get; set; }

    /// <summary>
    /// End line for partial read
    /// </summary>
    public int? EndLine { get; set; }
}

/// <summary>
/// File content request
/// </summary>
public class FileContentRequest
{
    public required string Path { get; init; }
    public FileContentOperation? Operation { get; set; } = FileContentOperation.Read;
    public string? Content { get; set; }
    public FileContentOptions? Options { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// File content response
/// </summary>
public class FileContentResponse
{
    public required string Path { get; set; }
    public string? Content { get; set; }
    public string? Encoding { get; set; }
    public long? Size { get; set; }
    public int? LineCount { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public int? TotalLines { get; set; }
    public long? BytesWritten { get; set; }
    public bool Created { get; set; }
    public FileContentOperation Operation { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// File operation request options
/// </summary>
public class FileOperationOptions
{
    /// <summary>
    /// Maximum file size for validation
    /// </summary>
    public long? MaxFileSize { get; set; }

    /// <summary>
    /// Require UTF-8 encoding
    /// </summary>
    public bool RequireUtf8 { get; set; } = false;

    /// <summary>
    /// Include new content in response
    /// </summary>
    public bool IncludeNewContent { get; set; } = false;

    /// <summary>
    /// Overwrite existing files for copy/move
    /// </summary>
    public bool Overwrite { get; set; } = false;

    /// <summary>
    /// Create backup before operation
    /// </summary>
    public bool CreateBackup { get; set; } = false;

    /// <summary>
    /// Dry run - don't actually make changes
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Create a new FileOperationOptions with the specified DryRun value
    /// </summary>
    internal FileOperationOptions WithDryRun(bool dryRun)
    {
        return new FileOperationOptions
        {
            MaxFileSize = MaxFileSize,
            RequireUtf8 = RequireUtf8,
            IncludeNewContent = IncludeNewContent,
            Overwrite = Overwrite,
            CreateBackup = CreateBackup,
            DryRun = dryRun
        };
    }
}

/// <summary>
/// File operation request
/// </summary>
public class FileOperationRequest
{
    public required string Path { get; init; }
    public required FileOperationType Operation { get; init; }
    public string? DestinationPath { get; set; }
    public List<TextEdit>? Edits { get; set; }
    public FileOperationOptions? Options { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// File operation response
/// </summary>
public class FileOperationResponse
{
    public required string Path { get; init; }
    public FileOperationType Operation { get; init; }
    public string? DestinationPath { get; init; }
    public int? EditsApplied { get; init; }
    public string? NewContent { get; init; }
    public long? BytesProcessed { get; init; }
    public bool IsValid { get; init; }
    public List<ValidationResult>? ValidationResults { get; init; }
    public ResponseMetadata Metadata { get; init; } = new();
    public string? Error { get; set; }
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>
/// File operation definition for batch operations
/// </summary>
public class FileOperationDefinition
{
    public required string Path { get; init; }
    public required FileOperationType Type { get; init; }
    public string? DestinationPath { get; set; }
    public List<TextEdit>? Edits { get; set; }
    public FileOperationOptions? Options { get; set; }
}

/// <summary>
/// File operation result for batch operations
/// </summary>
public class FileOperationResult
{
    public required string Path { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public long? BytesProcessed { get; init; }
    public FileOperationType Operation { get; init; }
}

/// <summary>
/// File batch request
/// </summary>
public class FileBatchRequest
{
    public required List<FileOperationDefinition> Operations { get; init; }
    public bool EnableTransactions { get; set; } = false;
    public bool FailFast { get; set; } = true;
    public bool ContinueOnError { get; set; } = false;
    public string? RequestId { get; set; }
}

/// <summary>
/// Batch operation summary
/// </summary>
public class BatchSummary
{
    public int TotalOperations { get; init; }
    public int SuccessfulOperations { get; init; }
    public int FailedOperations { get; init; }
    public long TotalBytesProcessed { get; init; }
}

/// <summary>
/// File batch response
/// </summary>
public class FileBatchResponse
{
    public required List<FileOperationResult> Operations { get; init; }
    public BatchSummary? Summary { get; set; }
    public required string RequestId { get; init; }
    public string? TransactionId { get; init; }
    public bool RolledBack { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }

    /// <summary>
    /// Whether the batch operation was successful
    /// </summary>
    public bool Success => string.IsNullOrEmpty(Error);

    /// <summary>
    /// Error message (alias for Error property)
    /// </summary>
    public string? ErrorMessage => Error;
}