namespace MCPsharp.Models;

/// <summary>
/// Represents the context of an opened project
/// </summary>
public class ProjectContext
{
    /// <summary>
    /// Root path of the project
    /// </summary>
    public string? RootPath { get; init; }

    /// <summary>
    /// Timestamp when the project was opened
    /// </summary>
    public DateTime? OpenedAt { get; init; }

    /// <summary>
    /// Number of files discovered in the project
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Set of known file paths in the project
    /// </summary>
    public HashSet<string> KnownFiles { get; init; } = new();
}
