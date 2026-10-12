using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public sealed partial class BulkEditService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to resolve pattern {Pattern}")]
    private static partial void LogResolvePatternFailed(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check file size for {FilePath}")]
    private static partial void LogCheckFileSizeFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating preview {PreviewId} for bulk edit operation")]
    private static partial void LogGeneratingPreview(ILogger logger, string previewId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate preview {PreviewId}")]
    private static partial void LogPreviewFailed(ILogger logger, Exception exception, string previewId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate preview for file {FilePath}")]
    private static partial void LogFilePreviewFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for bulk replace")]
    private static partial void LogBulkReplaceFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for conditional edit")]
    private static partial void LogConditionalEditFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for batch refactor")]
    private static partial void LogBatchRefactorFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for multi-file edit")]
    private static partial void LogMultiFileEditFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid file pattern {Pattern}")]
    private static partial void LogInvalidFilePattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid regex pattern {Pattern}")]
    private static partial void LogInvalidRegexPattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bulk edit validation failed")]
    private static partial void LogBulkEditValidationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to estimate impact")]
    private static partial void LogEstimateImpactFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to analyze file {FilePath}")]
    private static partial void LogAnalyzeFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get file statistics")]
    private static partial void LogFileStatisticsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting rollback operation {OperationId} for rollback session {RollbackId}")]
    private static partial void LogStartingRollback(ILogger logger, string operationId, string rollbackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cleanup rollback directory {Directory}")]
    private static partial void LogRollbackDirectoryCleanupFailed(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollback operation {OperationId} failed")]
    private static partial void LogRollbackFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get available rollbacks")]
    private static partial void LogGetRollbacksFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cleanup expired rollback {RollbackId}")]
    private static partial void LogExpiredRollbackCleanupFailed(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired rollback sessions")]
    private static partial void LogExpiredRollbacksCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create backup for file {FilePath}")]
    private static partial void LogBackupFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rollback file {FilePath}")]
    private static partial void LogRollbackFileFailed(ILogger logger, Exception exception, string filePath);
}
