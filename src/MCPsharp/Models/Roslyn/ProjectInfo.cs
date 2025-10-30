namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Information about a parsed project
/// </summary>
public class ProjectInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string TargetFramework { get; init; }
    public List<string> SourceFiles { get; init; } = new();
    public List<string> References { get; init; } = new();
    public int FileCount { get; init; }
}
