using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

public partial class StreamProcessingController
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream processing failed for operation {OperationId}")]
    private static partial void LogStreamProcessingFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing stream {OperationId}")]
    private static partial void LogErrorProcessingStream(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error in stream processing {OperationId}")]
    private static partial void LogFatalErrorInStreamProcessing(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error monitoring stream {OperationId}")]
    private static partial void LogErrorMonitoringStream(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error managing stream operations")]
    private static partial void LogErrorManagingStreamOperations(ILogger logger, Exception exception);
}
