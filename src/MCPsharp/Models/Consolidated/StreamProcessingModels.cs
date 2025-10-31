namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Stream operation types
/// </summary>
public enum StreamOperationType
{
    Process,
    Transform,
    Convert,
    Compress,
    Decompress,
    Validate,
    Analyze
}

/// <summary>
/// Stream status
/// </summary>
public enum StreamStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    NotFound
}

/// <summary>
/// Stream input types
/// </summary>
public enum StreamInputType
{
    File,
    Directory,
    MultipleFiles,
    Stream
}

/// <summary>
/// Stream management actions
/// </summary>
public enum StreamManagementAction
{
    List,
    Cancel,
    Pause,
    Resume,
    Cleanup,
    RegisterProcessor,
    UnregisterProcessor
}

/// <summary>
/// Processor options for stream processing
/// </summary>
public class ProcessorOptions
{
    /// <summary>
    /// Chunk size for processing
    /// </summary>
    public int ChunkSize { get; set; } = 65536;

    /// <summary>
    /// Enable compression
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Enable checkpoints for recovery
    /// </summary>
    public bool EnableCheckpoints { get; set; } = false;

    /// <summary>
    /// Maximum parallel chunks
    /// </summary>
    public int MaxParallelChunks { get; set; } = 4;

    /// <summary>
    /// Processor-specific parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Encoding for text processors
    /// </summary>
    public string Encoding { get; set; } = "utf-8";

    /// <summary>
    /// Buffer size
    /// </summary>
    public int BufferSize { get; set; } = 8192;

    /// <summary>
    /// Skip errors and continue processing
    /// </summary>
    public bool SkipErrors { get; set; } = false;

    /// <summary>
    /// Maximum errors before stopping
    /// </summary>
    public int MaxErrors { get; init; } = 100;
}

/// <summary>
/// Processing context for stream operations
/// </summary>
public class ProcessingContext
{
    public required string FilePath { get; init; }
    public required string OperationId { get; init; }
    public required int ChunkSize { get; init; }
    public required bool EnableCompression { get; init; }
    public required bool EnableCheckpoints { get; init; }
    public string? CheckpointPath { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Processing result
/// </summary>
public class ProcessingResult
{
    public required bool Success { get; init; }
    public long? RecordsProcessed { get; init; }
    public long? BytesProcessed { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public Dictionary<string, object>? Metrics { get; init; }
    public List<ProcessingWarning>? Warnings { get; init; }
}

/// <summary>
/// Processing warning
/// </summary>
public class ProcessingWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string FilePath { get; init; }
    public int? LineNumber { get; init; }
    public long? Position { get; init; }
}

/// <summary>
/// Processor capabilities
/// </summary>
public class ProcessorCapabilities
{
    public required List<string> SupportedInputFormats { get; init; }
    public required List<string> SupportedOutputFormats { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool SupportsParallelProcessing { get; init; }
    public bool SupportsCheckpoints { get; init; }
    public bool SupportsCompression { get; init; }
    public long MaxFileSize { get; init; }
    public List<string> RequiredParameters { get; init; } = new();
    public List<string> OptionalParameters { get; init; } = new();
}

/// <summary>
/// Stream process request
/// </summary>
public class StreamProcessRequest
{
    public required StreamInputType InputType { get; set; }
    public required string ProcessorType { get; set; }
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public string? FilePath { get; set; } // Alias for InputPath for compatibility
    public List<string>? Files { get; set; }
    public Stream? InputStream { get; set; }
    public ProcessorOptions? ProcessorOptions { get; set; }
    public int? ChunkSize { get; set; }
    public bool EnableCompression { get; set; } = false;
    public bool EnableCheckpoints { get; set; } = false;
    public bool Recursive { get; set; } = false;
    public string? SearchPattern { get; set; }
    public string? OperationId { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// Stream process result
/// </summary>
public class StreamProcessResult
{
    public required string InputPath { get; set; }
    public required string ProcessorType { get; set; }
    public string? OutputPath { get; set; }
    public required bool Success { get; set; }
    public long? BytesProcessed { get; set; }
    public long? RecordsProcessed { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Metrics { get; set; }
    public List<ProcessingWarning>? Warnings { get; set; }
}

/// <summary>
/// Stream process summary
/// </summary>
public class StreamProcessSummary
{
    public required int TotalFiles { get; init; }
    public required int SuccessfulFiles { get; init; }
    public required int FailedFiles { get; init; }
    public required long TotalBytesProcessed { get; init; }
    public required long TotalRecordsProcessed { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public double ProcessingRate => TotalBytesProcessed / ExecutionTime.TotalSeconds;
}

/// <summary>
/// Stream process response
/// </summary>
public class StreamProcessResponse
{
    public required string OperationId { get; set; }
    public required string ProcessorType { get; set; }
    public List<StreamProcessResult>? Results { get; set; }
    public StreamProcessSummary? Summary { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool Success { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Error message (alias for Error property)
    /// </summary>
    public string? ErrorMessage => Error;
}

/// <summary>
/// Stream monitor request
/// </summary>
public class StreamMonitorRequest
{
    public bool IncludeDetails { get; init; } = true;
    public bool IncludeLogs { get; init; } = true;
    public bool IncludePerformance { get; init; } = true;
    public int? MaxLogEntries { get; init; }
    public string? RequestId { get; init; }
}

/// <summary>
/// Stream progress information
/// </summary>
public class StreamProgressInfo
{
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required double PercentComplete { get; init; }
    public required string CurrentFile { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
    public required TimeSpan? EstimatedTimeRemaining { get; init; }
    public required string Message { get; init; }
    public required double ProcessingRate { get; init; }
    public required long MemoryUsage { get; init; }
    public required double CpuUsage { get; init; }
    public int ActiveThreads { get; init; }
    public long BytesReadPerSecond { get; init; }
    public long BytesWrittenPerSecond { get; init; }
    public int RecordsPerSecond { get; init; }
}

/// <summary>
/// Stream metrics
/// </summary>
public class StreamMetrics
{
    public required long BytesRead { get; init; }
    public required long BytesWritten { get; init; }
    public required long RecordsProcessed { get; init; }
    public required int ChunksProcessed { get; init; }
    public required double AverageProcessingRate { get; init; }
    public required long PeakMemoryUsage { get; init; }
    public required double ProcessorUtilization { get; init; }
    public required TimeSpan IOWaitTime { get; init; }
    public int TotalErrors { get; init; }
    public int TotalWarnings { get; init; }
    public List<ProcessingError> Errors { get; init; } = new();
    public List<ProcessingWarning> Warnings { get; init; } = new();
}

/// <summary>
/// Processing error
/// </summary>
public class ProcessingError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string FilePath { get; init; }
    public required DateTime Timestamp { get; init; }
    public int? LineNumber { get; init; }
    public long? Position { get; init; }
    public string? StackTrace { get; init; }
}

/// <summary>
/// Stream performance data
/// </summary>
public class StreamPerformanceData
{
    public required double AverageThroughput { get; init; }
    public required double PeakThroughput { get; init; }
    public required TimeSpan AverageLatency { get; init; }
    public required TimeSpan PeakLatency { get; init; }
    public required double CpuUtilization { get; init; }
    public required long MemoryUsage { get; init; }
    public required int ThreadUtilization { get; init; }
    public required int DiskReadOps { get; init; }
    public required int DiskWriteOps { get; init; }
    public required long DiskReadBytes { get; init; }
    public required long DiskWriteBytes { get; init; }
    public List<PerformanceSnapshot> Snapshots { get; init; } = new();
}

/// <summary>
/// Performance snapshot
/// </summary>
public class PerformanceSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required double Throughput { get; init; }
    public required double CpuUsage { get; init; }
    public required long MemoryUsage { get; init; }
    public required int ActiveThreads { get; init; }
}

/// <summary>
/// Stream monitor response
/// </summary>
public class StreamMonitorResponse
{
    public required string OperationId { get; init; }
    public required StreamStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EstimatedCompletion { get; init; }
    public StreamProgressInfo? Progress { get; init; }
    public StreamMetrics? Metrics { get; init; }
    public StreamPerformanceData? Performance { get; init; }
    public List<string>? RecentLogs { get; init; }
    public required ResponseMetadata Metadata { get; init; } = new();
    public string? Error { get; init; }
    public string RequestId { get; init; } = string.Empty;
}

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