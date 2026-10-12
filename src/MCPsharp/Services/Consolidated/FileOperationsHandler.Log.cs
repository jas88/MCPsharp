using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

public partial class FileOperationsHandler
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling file_info request")]
    private static partial void LogErrorHandlingFileInfoRequest(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling file_content request")]
    private static partial void LogErrorHandlingFileContentRequest(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling file_operation request")]
    private static partial void LogErrorHandlingFileOperationRequest(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling file_batch request")]
    private static partial void LogErrorHandlingFileBatchRequest(ILogger logger, Exception exception);
}
