namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Bulk validation result
/// </summary>
internal sealed class BulkValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}
