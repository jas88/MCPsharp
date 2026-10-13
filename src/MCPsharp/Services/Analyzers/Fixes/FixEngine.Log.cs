using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers.Fixes;

public partial class FixEngine
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Previewing {FixCount} fixes for {IssueCount} issues")]
    private static partial void LogPreviewingFixes(ILogger logger, int fixCount, int issueCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error reading file for preview: {FilePath}")]
    private static partial void LogErrorReadingFileForPreview(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error generating preview for fix: {FixId}")]
    private static partial void LogErrorGeneratingPreview(ILogger logger, Exception exception, string fixId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error previewing fixes")]
    private static partial void LogErrorPreviewingFixes(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {FixCount} fixes in session {SessionId}")]
    private static partial void LogApplyingFixes(ILogger logger, int fixCount, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying fix: {FixId}")]
    private static partial void LogErrorApplyingFix(ILogger logger, Exception exception, string fixId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying fixes in session {SessionId}")]
    private static partial void LogErrorApplyingFixesInSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting conflicts")]
    private static partial void LogErrorDetectingConflicts(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resolving conflict: {ConflictId}")]
    private static partial void LogErrorResolvingConflict(ILogger logger, Exception exception, string conflictId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resolving conflicts")]
    private static partial void LogErrorResolvingConflicts(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rolling back fixes for session {SessionId}")]
    private static partial void LogRollingBackFixes(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error restoring file: {FilePath}")]
    private static partial void LogErrorRestoringFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error rolling back session {SessionId}")]
    private static partial void LogErrorRollingBackSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error reading file for validation: {FilePath}")]
    private static partial void LogErrorReadingFileForValidation(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating fixes for session {SessionId}")]
    private static partial void LogErrorValidatingFixes(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating backup for file: {FilePath}")]
    private static partial void LogErrorCreatingBackup(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying edit for fix {FixId} to file {FilePath}")]
    private static partial void LogErrorApplyingEdit(ILogger logger, Exception exception, string fixId, string? filePath);
}
