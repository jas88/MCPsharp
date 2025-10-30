namespace MCPsharp.Models;

/// <summary>
/// Result of a file operation
/// </summary>
public class FileOperationResult
{
    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// File path that was operated on
    /// </summary>
    public required string Path { get; init; }
}

/// <summary>
/// Result of a file read operation
/// </summary>
public class FileReadResult : FileOperationResult
{
    /// <summary>
    /// File content as text
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Detected encoding
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// Number of lines in the file
    /// </summary>
    public int LineCount { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }
}

/// <summary>
/// Result of a file write operation
/// </summary>
public class FileWriteResult : FileOperationResult
{
    /// <summary>
    /// Number of bytes written
    /// </summary>
    public long BytesWritten { get; init; }

    /// <summary>
    /// Whether a new file was created (vs overwritten)
    /// </summary>
    public bool Created { get; init; }
}

/// <summary>
/// Result of a file edit operation
/// </summary>
public class FileEditResult : FileOperationResult
{
    /// <summary>
    /// Number of edits applied
    /// </summary>
    public int EditsApplied { get; init; }

    /// <summary>
    /// New content after edits
    /// </summary>
    public string? NewContent { get; init; }
}

/// <summary>
/// Result of a file list operation
/// </summary>
public class FileListResult
{
    /// <summary>
    /// List of files matching the criteria
    /// </summary>
    public required IReadOnlyList<FileInfo> Files { get; init; }

    /// <summary>
    /// Total number of files found
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Pattern that was used for filtering
    /// </summary>
    public string? Pattern { get; init; }
}
