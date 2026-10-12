namespace MCPsharp.Services.AI;

/// <summary>
/// Result of AI code transformation.
/// </summary>
public class CodeTransformationResult
{
    public string FilePath { get; set; } = "";
    public string OriginalCode { get; set; } = "";
    public string TransformedCode { get; set; } = "";
    public bool IsValid { get; set; }
    public string Description { get; set; } = "";
    public string AIExplanation { get; set; } = "";
    public List<CodeChange> Changes { get; set; } = new();
}
