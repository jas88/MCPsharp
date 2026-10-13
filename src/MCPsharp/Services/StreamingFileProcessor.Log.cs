using System;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public partial class StreamingFileProcessor
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transform failed for file: {FilePath}")]
    private static partial void LogTransformFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not estimate processing time for {FilePath}; using default")]
    private static partial void LogProcessingTimeEstimateFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Divide by zero in full processing, falling back to simple processing")]
    private static partial void LogDivideByZeroInFull(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during file stream processing")]
    private static partial void LogErrorDuringFileStreamProcessing(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully processed file {FilePath} to {OutputPath} in {ProcessingTime}")]
    private static partial void LogSuccessfullyProcessedFileToIn(ILogger logger, string filePath, string outputPath, TimeSpan processingTime);

    [LoggerMessage(Level = LogLevel.Information, Message = "Operation {OperationId} was {Reason}")]
    private static partial void LogOperationWas(ILogger logger, string operationId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt {Attempt} failed for file {FilePath}")]
    private static partial void LogAttemptFailedForFile(ILogger logger, Exception exception, int attempt, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} after {AttemptCount} attempts")]
    private static partial void LogFailedToProcessFileAfter(ILogger logger, Exception exception, string filePath, int attemptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting bulk transformation of {Count} files with {MaxDegree} parallel workers")]
    private static partial void LogStartingBulkTransformationOfFiles(ILogger logger, int count, int maxDegree);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk transformation completed. Processed {Count} files with {SuccessCount} successes")]
    private static partial void LogBulkTransformationCompletedProcessedFiles(ILogger logger, int count, int successCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled operation {OperationId}")]
    private static partial void LogCancelledOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation {OperationId} not found for resume")]
    private static partial void LogOperationNotFoundForResume(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation {OperationId} has no checkpoint to resume from")]
    private static partial void LogOperationHasNoCheckpointTo(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully resumed operation {OperationId}")]
    private static partial void LogSuccessfullyResumedOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resume operation {OperationId}")]
    private static partial void LogFailedToResumeOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created checkpoint {CheckpointId} for operation {OperationId} at position {Position}")]
    private static partial void LogCreatedCheckpointForOperationAt(ILogger logger, string checkpointId, string operationId, long position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} completed operations older than {OlderThan}")]
    private static partial void LogCleanedUpCompletedOperationsOlder(ILogger logger, int count, TimeSpan olderThan);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating StreamResult - ProcessedBytes: {ProcessedBytes}, ProcessingTime: {ProcessingTime} seconds, TotalSeconds: {TotalSeconds}")]
    private static partial void LogErrorCreatingStreamResultProcessedBytesProcessingTime(ILogger logger, Exception exception, long processedBytes, TimeSpan processingTime, double totalSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to save checkpoint {CheckpointId}")]
    private static partial void LogFailedToSaveCheckpoint(ILogger logger, Exception exception, string checkpointId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during automatic cleanup")]
    private static partial void LogErrorDuringAutomaticCleanup(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Divide by zero error processing file {FilePath}, falling back to simple processing")]
    private static partial void LogDivideByZeroErrorProcessing(ILogger logger, DivideByZeroException exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fallback simple processing also failed for file {FilePath}")]
    private static partial void LogFallbackSimpleProcessingAlsoFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing file {FilePath}")]
    private static partial void LogErrorProcessingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file with transform: {FilePath}")]
    private static partial void LogFailedToProcessFileWith(ILogger logger, Exception exception, string filePath);
}
