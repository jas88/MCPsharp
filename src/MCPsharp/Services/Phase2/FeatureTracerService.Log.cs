using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase2;

public partial class FeatureTracerService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Tracing feature: {FeatureName}")]
    private static partial void LogTracingFeature(ILogger logger, string featureName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature trace completed for {FeatureName}: found {ComponentCount} components")]
    private static partial void LogFeatureTraceCompleted(ILogger logger, string featureName, int componentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovering feature components from entry point: {EntryPoint}")]
    private static partial void LogDiscoveringFeatureComponents(ILogger logger, string entryPoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature discovery completed for {FeatureName} from entry point {EntryPoint}")]
    private static partial void LogFeatureDiscoveryCompleted(ILogger logger, string featureName, string entryPoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching pattern {Pattern}")]
    private static partial void LogErrorSearchingPattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error categorizing file {FilePath}")]
    private static partial void LogErrorCategorizingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for feature symbols")]
    private static partial void LogErrorSearchingFeatureSymbols(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error analyzing call chain for {SymbolName}")]
    private static partial void LogErrorAnalyzingCallChain(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error analyzing type usages for {SymbolName}")]
    private static partial void LogErrorAnalyzingTypeUsages(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {PathCount} call chains for feature discovery")]
    private static partial void LogProcessedCallChains(ILogger logger, int pathCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error processing call chain results")]
    private static partial void LogErrorProcessingCallChain(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {UsageCount} type usages for feature discovery")]
    private static partial void LogProcessedTypeUsages(ILogger logger, int usageCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error processing type usage results")]
    private static partial void LogErrorProcessingTypeUsages(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for test files with pattern {Pattern}")]
    private static partial void LogErrorSearchingTestFiles(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for config files with pattern {Pattern}")]
    private static partial void LogErrorSearchingConfigFiles(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error checking appsettings files for feature configuration")]
    private static partial void LogErrorCheckingAppSettings(ILogger logger, Exception exception);
}
