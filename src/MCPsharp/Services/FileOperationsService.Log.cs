using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public partial class FileOperationsService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to update Roslyn workspace for {RelativePath}: {ErrorMessage}")]
    private static partial void LogFailedToUpdateRoslynWorkspace(ILogger logger, Exception exception, string relativePath, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to {Operation} file: {RelativePath}")]
    private static partial void LogFileOperationFailed(ILogger logger, Exception exception, string operation, string relativePath);
}
