namespace MCPsharp.Models.Database;

/// <summary>
/// Represents a cached project in the database.
/// </summary>
public sealed class DbProject
{
    public long Id { get; set; }
    public required string RootPathHash { get; set; }
    public required string RootPath { get; set; }
    public required string Name { get; set; }
    public DateTime OpenedAt { get; set; }
    public int SolutionCount { get; set; }
    public int ProjectCount { get; set; }
    public int FileCount { get; set; }
}
