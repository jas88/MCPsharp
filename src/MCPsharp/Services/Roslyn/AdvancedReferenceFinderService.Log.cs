using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Source-generated logging methods.
/// </summary>
public partial class AdvancedReferenceFinderService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Caller analysis failed for method {MethodName}")]
    private static partial void LogCallerAnalysisFailed(ILogger logger, string methodName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call chain analysis failed for method {MethodName}")]
    private static partial void LogCallChainAnalysisFailed(ILogger logger, string methodName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Type usage analysis failed for type {TypeName}")]
    private static partial void LogTypeUsageAnalysisFailed(ILogger logger, string typeName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call pattern analysis failed for method {MethodName}")]
    private static partial void LogCallPatternAnalysisFailed(ILogger logger, string methodName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inheritance analysis failed for type {TypeName}")]
    private static partial void LogInheritanceAnalysisFailed(ILogger logger, string typeName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Type dependency analysis failed for type {TypeName}")]
    private static partial void LogTypeDependencyAnalysisFailed(ILogger logger, string typeName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to determine reverse search capabilities")]
    private static partial void LogCapabilitiesFailed(ILogger logger, Exception exception);
}
