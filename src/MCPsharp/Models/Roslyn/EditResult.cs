namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of a semantic edit operation
/// </summary>
public class EditResult
{
    public bool Success { get; init; }
    public string? ClassName { get; init; }
    public string? MemberName { get; init; }
    public LocationInfo? InsertedAt { get; init; }
    public string? GeneratedCode { get; init; }
    public string? Error { get; init; }
}
