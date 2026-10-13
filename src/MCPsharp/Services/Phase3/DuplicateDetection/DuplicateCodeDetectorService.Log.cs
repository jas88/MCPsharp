using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Logging for <see cref="DuplicateCodeDetectorService"/>
/// </summary>
public partial class DuplicateCodeDetectorService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting duplicate code detection for project: {ProjectPath}")]
    private static partial void LogStartingDuplicateDetection(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate detection completed in {Duration}ms. Found {GroupCount} duplicate groups affecting {FileCount} files.")]
    private static partial void LogDuplicateDetectionCompleted(ILogger logger, double duration, int groupCount, int fileCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during duplicate code detection")]
    private static partial void LogDuplicateDetectionError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting duplicate detection for {FileCount} specific files")]
    private static partial void LogStartingFileDuplicateDetection(ILogger logger, int fileCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during duplicate detection in specific files")]
    private static partial void LogFileDuplicateDetectionError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialize Roslyn workspace for path: {ProjectPath}")]
    private static partial void LogWorkspaceInitializationFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No compilation available for workspace")]
    private static partial void LogNoCompilation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract code blocks from file: {FilePath}")]
    private static partial void LogCodeBlockExtractionFailed(ILogger logger, Exception exception, string filePath);
}
