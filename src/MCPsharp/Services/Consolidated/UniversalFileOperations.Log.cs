using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class UniversalFileOperations
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to {Operation} file: {Path}")]
    private static partial void LogFileOperationFailed(ILogger logger, Exception exception, string operation, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored exception while {Activity}: {Path}")]
    private static partial void LogIgnoredException(ILogger logger, Exception exception, string activity, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting file info for {Path}")]
    private static partial void LogErrorGettingFileInfoFor(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling file content for {Path}")]
    private static partial void LogErrorHandlingFileContentFor(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing file operation {Operation} on {Path}")]
    private static partial void LogErrorExecutingFileOperationOn(ILogger logger, Exception exception, FileOperationType operation, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing batch operations")]
    private static partial void LogErrorExecutingBatchOperations(ILogger logger, Exception exception);
}
