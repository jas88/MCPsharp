using MCPsharp.Models.Streaming;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public partial class StreamOperationManager
{
    [LoggerMessage(Level = LogLevel.Information, Message = "StreamOperationManager initialized with max {MaxConcurrent} concurrent operations")]
    private static partial void LogStreamOperationManagerInitializedWithMaxConcurrent(ILogger logger, int maxConcurrent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created streaming operation {OperationId} ({Name}) for file {FilePath}")]
    private static partial void LogCreatedStreamingOperationForFile(ILogger logger, string operationId, string name, string? filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting streaming operation {OperationId} ({Name})")]
    private static partial void LogStartingStreamingOperation(ILogger logger, string operationId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Streaming operation {OperationId} completed with status {Status}: {Message}")]
    private static partial void LogStreamingOperationCompletedWithStatus(ILogger logger, string operationId, StreamOperationStatus status, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Streaming operation {OperationId} was cancelled")]
    private static partial void LogStreamingOperationWasCancelled(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Streaming operation {OperationId} failed")]
    private static partial void LogStreamingOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled streaming operation {OperationId}")]
    private static partial void LogCancelledStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error cancelling operation {OperationId}")]
    private static partial void LogErrorCancellingOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Paused streaming operation {OperationId}")]
    private static partial void LogPausedStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resumed streaming operation {OperationId}")]
    private static partial void LogResumedStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created checkpoint {CheckpointId} for operation {OperationId}")]
    private static partial void LogCreatedCheckpointForOperation(ILogger logger, string checkpointId, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating checkpoint for operation {OperationId}")]
    private static partial void LogErrorCreatingCheckpointForOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resuming operation {OperationId} from checkpoint {CheckpointId}")]
    private static partial void LogResumingOperationFromCheckpoint(ILogger logger, string operationId, string checkpointId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resuming operation {OperationId} from checkpoint {CheckpointId}")]
    private static partial void LogErrorResumingOperationFromCheckpoint(ILogger logger, Exception exception, string operationId, string checkpointId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error cleaning up operation {OperationId}")]
    private static partial void LogErrorCleaningUpOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} streaming operations")]
    private static partial void LogCleanedUpStreamingOperations(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error deleting checkpoint file {FilePath}")]
    private static partial void LogErrorDeletingCheckpointFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during automatic cleanup of streaming operations")]
    private static partial void LogErrorDuringAutomaticCleanupOf(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing operation {OperationId}")]
    private static partial void LogErrorDisposingOperation(ILogger logger, Exception exception, string operationId);
}
