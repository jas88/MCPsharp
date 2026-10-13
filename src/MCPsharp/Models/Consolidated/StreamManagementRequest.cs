namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Stream management request
/// </summary>
public class StreamManagementRequest
{
    public required StreamManagementAction Action { get; set; }
    public string? Target { get; set; }
    public string? OperationId { get; set; } // Alias for Target for compatibility
    public TimeSpan? MaxAge { get; set; }
    public double? CleanupOlderThanHours { get; set; } // For cleanup operations
    public StreamProcessorDefinition? ProcessorDefinition { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// Active stream information
/// </summary>
public class ActiveStreamInfo
{
    public required string Id { get; init; }
    public required StreamOperationType Type { get; init; }
    public required StreamStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public required string ProcessorType { get; init; }
    public required long BytesProcessed { get; init; }
    public required string CurrentFile { get; init; }
    public double Progress { get; init; }
    public TimeSpan RunningTime => DateTime.UtcNow - CreatedAt;
}

/// <summary>
/// Stream processor definition
/// </summary>
public class StreamProcessorDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string AssemblyPath { get; init; }
    public required string ClassName { get; init; }
    public required ProcessorCapabilities Capabilities { get; init; }
    public Dictionary<string, object> Configuration { get; init; } = new();
    public string Version { get; init; } = "1.0.0";
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Stream cancel result
/// </summary>
public class StreamCancelResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? CancelledAt { get; init; }
    public int? CompletedChunks { get; init; }
    public long? ProcessedBytes { get; init; }
}

/// <summary>
/// Stream pause result
/// </summary>
public class StreamPauseResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? PausedAt { get; init; }
    public int? CompletedChunks { get; init; }
    public long? ProcessedBytes { get; init; }
    public bool CheckpointCreated { get; init; }
    public string? CheckpointPath { get; init; }
}

/// <summary>
/// Stream resume result
/// </summary>
public class StreamResumeResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? ResumedAt { get; init; }
    public bool CheckpointLoaded { get; init; }
    public string? CheckpointPath { get; init; }
    public int? ResumedFromChunk { get; init; }
}

/// <summary>
/// Stream cleanup result
/// </summary>
public class StreamCleanupResult
{
    public required bool Success { get; init; }
    public required int CleanedOperations { get; init; }
    public required string Message { get; init; }
    public long FreedSpaceBytes { get; init; }
    public int CleanedTempFiles { get; init; }
    public int CleanedCheckpoints { get; init; }
    public List<string> CleanedOperationIds { get; init; } = new();
}

/// <summary>
/// Stream register result
/// </summary>
public class StreamRegisterResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? RegisteredProcessorId { get; init; }
    public ProcessorCapabilities? Capabilities { get; init; }
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Stream unregister result
/// </summary>
public class StreamUnregisterResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public bool WasActive { get; init; }
    public int? CancelledOperations { get; init; }
}

/// <summary>
/// Stream management response
/// </summary>
public class StreamManagementResponse
{
    public required StreamManagementAction Action { get; set; }
    public string? Target { get; set; }
    public List<ActiveStreamInfo>? ActiveStreams { get; set; }
    public StreamCancelResult? CancelResult { get; set; }
    public StreamPauseResult? PauseResult { get; set; }
    public StreamResumeResult? ResumeResult { get; set; }
    public StreamCleanupResult? CleanupResult { get; set; }
    public StreamRegisterResult? RegisterResult { get; set; }
    public StreamUnregisterResult? UnregisterResult { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the management operation was successful
    /// </summary>
    public bool Success => string.IsNullOrEmpty(Error);

    /// <summary>
    /// Error message (alias for Error property)
    /// </summary>
    public string? ErrorMessage => Error;
}
