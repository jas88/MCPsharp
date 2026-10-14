using MCPsharp.Models.Consolidated;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing symbol {Symbol}")]
    private static partial void LogErrorAnalyzingSymbol(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing type {Type}")]
    private static partial void LogErrorAnalyzingType(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file {File}")]
    private static partial void LogErrorAnalyzingFile(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing project {Project}")]
    private static partial void LogErrorAnalyzingProject(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing architecture for {Scope}")]
    private static partial void LogErrorAnalyzingArchitectureFor(ILogger logger, Exception exception, AnalysisScope scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing dependencies for {Scope}")]
    private static partial void LogErrorAnalyzingDependenciesFor(ILogger logger, Exception exception, AnalysisScope scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing quality for {Scope}")]
    private static partial void LogErrorAnalyzingQualityFor(ILogger logger, Exception exception, AnalysisScope scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting symbol definition for {Symbol}")]
    private static partial void LogErrorGettingSymbolDefinitionFor(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting symbol references for {Symbol}")]
    private static partial void LogErrorGettingSymbolReferencesFor(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error processing caller for reference at {File}:{Line}")]
    private static partial void LogErrorProcessingCallerForReference(ILogger logger, Exception exception, string file, int line);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting symbol callers for {Symbol}")]
    private static partial void LogErrorGettingSymbolCallersFor(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing symbol usage patterns for {Symbol}")]
    private static partial void LogErrorAnalyzingSymbolUsagePatterns(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding related symbols for {Symbol}")]
    private static partial void LogErrorFindingRelatedSymbolsFor(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating symbol metrics for {Symbol}")]
    private static partial void LogErrorCalculatingSymbolMetricsFor(ILogger logger, Exception exception, string symbol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting type structure for {Type}")]
    private static partial void LogErrorGettingTypeStructureFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting type inheritance for {Type}")]
    private static partial void LogErrorGettingTypeInheritanceFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting type usages for {Type}")]
    private static partial void LogErrorGettingTypeUsagesFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting type implementations for {Type}")]
    private static partial void LogErrorGettingTypeImplementationsFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing type members for {Type}")]
    private static partial void LogErrorAnalyzingTypeMembersFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating type metrics for {Type}")]
    private static partial void LogErrorCalculatingTypeMetricsFor(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting file basic info for {File}")]
    private static partial void LogErrorGettingFileBasicInfo(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file structure for {File}")]
    private static partial void LogErrorAnalyzingFileStructureFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file complexity for {File}")]
    private static partial void LogErrorAnalyzingFileComplexityFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file dependencies for {File}")]
    private static partial void LogErrorAnalyzingFileDependenciesFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing file quality for {File}")]
    private static partial void LogErrorAnalyzingFileQualityFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting file issues for {File}")]
    private static partial void LogErrorDetectingFileIssuesFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error generating file suggestions for {File}")]
    private static partial void LogErrorGeneratingFileSuggestionsFor(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting project structure for {Project}")]
    private static partial void LogErrorGettingProjectStructureFor(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting project file inventory for {Project}")]
    private static partial void LogErrorGettingProjectFileInventory(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing project dependencies for {Project}")]
    private static partial void LogErrorAnalyzingProjectDependenciesFor(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing project build info for {Project}")]
    private static partial void LogErrorAnalyzingProjectBuildInfo(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating project metrics for {Project}")]
    private static partial void LogErrorCalculatingProjectMetricsFor(ILogger logger, Exception exception, string project);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting default architecture definition")]
    private static partial void LogErrorGettingDefaultArchitectureDefinition(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating architecture metrics for {Scope}")]
    private static partial void LogErrorCalculatingArchitectureMetricsFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error building dependency graph for {Scope}")]
    private static partial void LogErrorBuildingDependencyGraphFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting circular dependencies for {Scope}")]
    private static partial void LogErrorDetectingCircularDependenciesFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing dependency impact for {Scope}")]
    private static partial void LogErrorAnalyzingDependencyImpactFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing critical paths for {Scope}")]
    private static partial void LogErrorAnalyzingCriticalPathsFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating dependency metrics")]
    private static partial void LogErrorCalculatingDependencyMetrics(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing complexity for {Scope}")]
    private static partial void LogErrorAnalyzingComplexityFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing maintainability for {Scope}")]
    private static partial void LogErrorAnalyzingMaintainabilityFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing test coverage for {Scope}")]
    private static partial void LogErrorAnalyzingTestCoverageFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting code smells for {Scope}")]
    private static partial void LogErrorDetectingCodeSmellsFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating quality score")]
    private static partial void LogErrorCalculatingQualityScore(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error generating quality recommendations")]
    private static partial void LogErrorGeneratingQualityRecommendations(ILogger logger, Exception exception);
}
