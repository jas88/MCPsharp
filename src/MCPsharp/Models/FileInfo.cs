namespace MCPsharp.Models;

/// <summary>
/// Information about a file in the project
/// </summary>
public class FileInfo
{
    /// <summary>
    /// Absolute path to the file
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Path relative to project root
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Last modified timestamp (UTC)
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// Whether this is a hidden file
    /// </summary>
    public bool IsHidden { get; init; }
}
