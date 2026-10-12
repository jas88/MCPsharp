namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Stream validation result
/// </summary>
internal sealed class StreamValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}
