namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Result of a reference search
/// </summary>
public class ReferenceResult
{
    public required string Symbol { get; init; }
    public List<ReferenceLocation> References { get; init; } = new();
    public int TotalReferences { get; init; }
}

/// <summary>
/// Single reference location
/// </summary>
public class ReferenceLocation
{
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public string? Context { get; init; }
}
