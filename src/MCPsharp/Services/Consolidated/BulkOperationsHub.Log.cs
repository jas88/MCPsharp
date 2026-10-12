using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

public partial class BulkOperationsHub
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Bulk operation failed for file {FilePath}")]
    private static partial void LogBulkFileOperationFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create bulk operation backup at {BackupPath}")]
    private static partial void LogBulkBackupFailed(ILogger logger, Exception exception, string backupPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing bulk operation {OperationId}")]
    private static partial void LogErrorExecutingBulkOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error in bulk operation {OperationId}")]
    private static partial void LogFatalErrorInBulkOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error previewing bulk operation")]
    private static partial void LogErrorPreviewingBulkOperation(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting bulk progress for {OperationId}")]
    private static partial void LogErrorGettingBulkProgressFor(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error managing bulk operation")]
    private static partial void LogErrorManagingBulkOperation(ILogger logger, Exception exception);
}
