using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Bulk operation context
/// </summary>
internal sealed class BulkOperationContext
{
    public required string Id { get; init; }
    public required BulkOperationType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public BulkOperationStatus Status { get; set; }
    public string? ProgressId { get; set; }
    public string? BackupId { get; set; }
}
