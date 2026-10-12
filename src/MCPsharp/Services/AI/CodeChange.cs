namespace MCPsharp.Services.AI;

/// <summary>
/// Individual code change from AI.
/// </summary>
public class CodeChange
{
    public string Type { get; set; } = ""; // replace, insert, delete
    public string Target { get; set; } = ""; // NodeType:NodeName
    public string? NewCode { get; set; }
}
