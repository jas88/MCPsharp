namespace MCPsharp.Models.Database;

/// <summary>
/// Represents a tracked file in the project cache.
/// </summary>
public sealed class DbFile
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public required string RelativePath { get; set; }
    public required string ContentHash { get; set; }
    public DateTime LastIndexed { get; set; }
    public long SizeBytes { get; set; }
    public required string Language { get; set; }
}
