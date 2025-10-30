using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MCPsharp.Models.Streaming;

/// <summary>
/// Request for processing a file using streaming operations
/// </summary>
public class StreamProcessRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public StreamProcessorType ProcessorType { get; set; }
    public Dictionary<string, object> ProcessorOptions { get; set; } = new();
    public long ChunkSize { get; set; } = 64 * 1024; // 64KB default
    public bool CreateCheckpoint { get; set; } = true;
    public bool EnableCompression { get; set; } = false;
    public string? CheckpointDirectory { get; set; }
}

/// <summary>
/// Types of streaming processors available
/// </summary>
public enum StreamProcessorType
{
    LineProcessor,
    RegexProcessor,
    CsvProcessor,
    BinaryProcessor,
    JsonProcessor,
    CustomProcessor
}

/// <summary>
/// Represents a streaming operation with state management
/// </summary>
public class StreamOperation
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public StreamOperationStatus Status { get; set; } = StreamOperationStatus.Created;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastCheckpointAt { get; set; }
    public StreamProcessRequest Request { get; set; } = new();
    public StreamProgress Progress { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public List<string> TemporaryFiles { get; set; } = new();
    public CheckpointData? LastCheckpoint { get; set; }
}

/// <summary>
/// Status of streaming operations
/// </summary>
public enum StreamOperationStatus
{
    Created,
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Resumed
}

/// <summary>
/// Progress tracking for streaming operations
/// </summary>
public class StreamProgress
{
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public long ChunksProcessed { get; set; }
    public long TotalChunks { get; set; }
    public long LinesProcessed { get; set; }
    public long ItemsProcessed { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public long ProcessingSpeedBytesPerSecond { get; set; }
    public string? CurrentPhase { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Data for checkpoint recovery
/// </summary>
public class CheckpointData
{
    public string CheckpointId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long Position { get; set; }
    public long BytesProcessed { get; set; }
    public long ChunksProcessed { get; set; }
    public long LinesProcessed { get; set; }
    public string? ProcessorState { get; set; }
    public Dictionary<string, object> CustomState { get; set; } = new();
    public string CheckpointFilePath { get; set; } = string.Empty;
    public string CheckpointDirectory { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>
/// Result of a streaming operation
/// </summary>
public class StreamResult
{
    public bool Success { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public long BytesProcessed { get; set; }
    public long ChunksProcessed { get; set; }
    public long LinesProcessed { get; set; }
    public long ItemsProcessed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public long ProcessingSpeedBytesPerSecond { get; set; }
    public List<string> OutputFiles { get; set; } = new();
    public List<string> TemporaryFiles { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    public CheckpointData? FinalCheckpoint { get; set; }
}

/// <summary>
/// Configuration for chunk processing
/// </summary>
public class ChunkProcessor
{
    public string Name { get; set; } = string.Empty;
    public StreamProcessorType ProcessorType { get; set; }
    public Dictionary<string, object> Options { get; set; } = new();
    public Func<StreamChunk, Task<StreamChunk>>? ProcessAsync { get; set; }
    public Func<StreamChunk, bool>? ShouldProcessChunk { get; set; }
    public bool EnableParallelProcessing { get; set; } = false;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// Represents a chunk of data being processed
/// </summary>
public class StreamChunk
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public long Position { get; set; }
    public long Length { get; set; }
    public long ChunkIndex { get; set; }
    public bool IsLastChunk { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? Encoding { get; set; }
    public List<string> Lines { get; set; } = new();
    public string? Checksum { get; set; }
}

/// <summary>
/// Request for bulk transformation operations
/// </summary>
public class BulkTransformRequest
{
    public List<string> InputFiles { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
    public StreamProcessorType ProcessorType { get; set; }
    public Dictionary<string, object> ProcessorOptions { get; set; } = new();
    public long ChunkSize { get; set; } = 64 * 1024;
    public bool EnableParallelProcessing { get; set; } = true;
    public bool EnableCompression { get; set; } = false;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public bool PreserveDirectoryStructure { get; set; } = true;
    public string? FilePattern { get; set; }
    public bool Recursive { get; set; } = false;
}

/// <summary>
/// Configuration for line-based processing
/// </summary>
public class LineProcessorConfig
{
    public string? LineFilter { get; set; }
    public string? LineReplacement { get; set; }
    public bool IncludeLineNumbers { get; set; } = false;
    public string? LinePrefix { get; set; }
    public string? LineSuffix { get; set; }
    public bool RemoveEmptyLines { get; set; } = false;
    public string? FieldSeparator { get; set; }
    public List<int> SelectedFields { get; set; } = new();
    public bool TrimWhitespace { get; set; } = true;
    public System.Text.Encoding? Encoding { get; set; } = System.Text.Encoding.UTF8;
}

/// <summary>
/// Configuration for regex-based processing
/// </summary>
public class RegexProcessorConfig
{
    public string Pattern { get; set; } = string.Empty;
    public string? Replacement { get; set; }
    public System.Text.RegularExpressions.RegexOptions Options { get; set; } = System.Text.RegularExpressions.RegexOptions.None;
    public bool Multiline { get; set; } = false;
    public bool IgnoreCase { get; set; } = false;
    public bool MatchWholeLine { get; set; } = false;
    public int MaxMatches { get; set; } = 0; // 0 = unlimited
}

/// <summary>
/// Configuration for CSV processing
/// </summary>
public class CsvProcessorConfig
{
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;
    public string QuoteCharacter { get; set; } = "\"";
    public string EscapeCharacter { get; set; } = "\\";
    public List<string> SelectedColumns { get; set; } = new();
    public List<int> SelectedColumnIndexes { get; set; } = new();
    public bool SkipEmptyRows { get; set; } = true;
    public bool TrimFields { get; set; } = true;
    public Dictionary<string, string>? ColumnMappings { get; set; }
    public Func<string[], bool>? RowFilter { get; set; }
}

/// <summary>
/// Configuration for binary file processing
/// </summary>
public class BinaryProcessorConfig
{
    public byte[]? SearchPattern { get; set; }
    public byte[]? ReplacementPattern { get; set; }
    public bool CalculateChecksums { get; set; } = true;
    public string ChecksumAlgorithm { get; set; } = "SHA256";
    public bool PreserveTimestamps { get; set; } = true;
    public Dictionary<string, object> CustomMetadata { get; set; } = new();
}