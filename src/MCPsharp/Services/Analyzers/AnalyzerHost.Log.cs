using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

public partial class AnalyzerHost
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Loading analyzer from assembly: {AssemblyPath}")]
    private static partial void LogLoadingAnalyzer(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzer assembly: {AssemblyPath}")]
    private static partial void LogErrorLoadingAnalyzer(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unloading analyzer: {AnalyzerId}")]
    private static partial void LogUnloadingAnalyzer(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error unloading analyzer: {AnalyzerId}")]
    private static partial void LogErrorUnloadingAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error setting analyzer enabled state: {AnalyzerId}")]
    private static partial void LogErrorSettingEnabledState(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error configuring analyzer: {AnalyzerId}")]
    private static partial void LogErrorConfiguringAnalyzer(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting analysis session {SessionId} with analyzer {AnalyzerId}")]
    private static partial void LogStartingAnalysisSession(ILogger logger, string sessionId, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File not found for analysis: {FilePath}")]
    private static partial void LogFileNotFoundForAnalysis(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} not allowed to read file: {FilePath}")]
    private static partial void LogReadNotAllowed(ILogger logger, string analyzerId, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file: {FilePath}")]
    private static partial void LogErrorAnalyzingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in analysis session {SessionId}")]
    private static partial void LogErrorInAnalysisSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting fixes for analyzer: {AnalyzerId}")]
    private static partial void LogErrorGettingFixes(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting fix session {SessionId} with analyzer {AnalyzerId}")]
    private static partial void LogStartingFixSession(ILogger logger, string sessionId, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in fix session {SessionId}")]
    private static partial void LogErrorInFixSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting analyzer health status")]
    private static partial void LogErrorGettingHealthStatus(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating fixes for session {SessionId}")]
    private static partial void LogGeneratingFixes(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running analyzer {AnalyzerId} on target {TargetPath}")]
    private static partial void LogRunningAnalyzer(ILogger logger, string analyzerId, string targetPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis cancelled during file processing: {FilePath}")]
    private static partial void LogAnalysisCancelledDuringFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzer execution was cancelled: {AnalyzerId}")]
    private static partial void LogAnalyzerExecutionCancelled(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error running analyzer {AnalyzerId} on target {TargetPath}")]
    private static partial void LogErrorRunningAnalyzer(ILogger logger, Exception exception, string analyzerId, string targetPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting loaded analyzers")]
    private static partial void LogErrorGettingLoadedAnalyzers(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting capabilities for analyzer: {AnalyzerId}")]
    private static partial void LogErrorGettingCapabilities(ILogger logger, Exception exception, string analyzerId);
}
