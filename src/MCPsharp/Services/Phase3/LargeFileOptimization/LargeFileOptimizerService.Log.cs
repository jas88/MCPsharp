using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Logging for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file {FilePath}")]
    private static partial void LogFileAnalysisError(ILogger logger, Exception exception, string filePath);
}
