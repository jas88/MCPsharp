using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.AI;

public partial class AICodeTransformationService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "AI suggesting fix for {FilePath}: {Description}")]
    private static partial void LogSuggestingFix(ILogger logger, string filePath, string description);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI refactoring {FilePath}: {Goal}")]
    private static partial void LogRefactoring(ILogger logger, string filePath, string goal);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI implementing feature in {FilePath}: {Feature}")]
    private static partial void LogImplementingFeature(ILogger logger, string filePath, string feature);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse AI code response: {Response}")]
    private static partial void LogParseResponseFailed(ILogger logger, Exception exception, string response);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transformed code has {ErrorCount} syntax errors")]
    private static partial void LogSyntaxErrors(ILogger logger, int errorCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transformed code has {ErrorCount} semantic errors (original had {OriginalErrorCount})")]
    private static partial void LogSemanticErrors(ILogger logger, int errorCount, int originalErrorCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "  {Error}")]
    private static partial void LogDiagnostic(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to validate transformed code")]
    private static partial void LogValidationFailed(ILogger logger, Exception exception);
}
