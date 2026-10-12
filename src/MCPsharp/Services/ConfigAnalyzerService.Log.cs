using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public partial class ConfigAnalyzerService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to validate configuration file: {FilePath}")]
    private static partial void LogConfigFileValidationFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping unreadable configuration file: {FilePath}")]
    private static partial void LogSkippedUnreadableConfigFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to validate cross-file configuration consistency")]
    private static partial void LogCrossFileValidationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzing configuration schema for file: {FilePath}")]
    private static partial void LogAnalyzingConfigurationSchemaForFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully analyzed configuration schema for {FilePath}, found {PropertyCount} properties")]
    private static partial void LogSuccessfullyAnalyzedConfigurationSchemaFor(ILogger logger, string filePath, int propertyCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to analyze configuration schema for file: {FilePath}")]
    private static partial void LogFailedToAnalyzeConfigurationSchema(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Merging {ConfigCount} configuration files")]
    private static partial void LogMergingConfigurationFiles(ILogger logger, int configCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuration file not found, skipping: {FilePath}")]
    private static partial void LogConfigurationFileNotFoundSkipping(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration conflict detected for key '{Key}' between files")]
    private static partial void LogConfigurationConflictDetectedForKey(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully processed configuration file: {FilePath}")]
    private static partial void LogSuccessfullyProcessedConfigurationFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process configuration file: {FilePath}")]
    private static partial void LogFailedToProcessConfigurationFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully merged {ConfigCount} configuration files with {ConflictCount} conflicts")]
    private static partial void LogSuccessfullyMergedConfigurationFilesWith(ILogger logger, int configCount, int conflictCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Validating configuration consistency for project: {ProjectRoot}")]
    private static partial void LogValidatingConfigurationConsistencyForProject(ILogger logger, string projectRoot);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration validation completed with {IssueCount} issues found")]
    private static partial void LogConfigurationValidationCompletedWithIssues(ILogger logger, int issueCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to validate configuration consistency for project: {ProjectRoot}")]
    private static partial void LogFailedToValidateConfigurationConsistency(ILogger logger, Exception exception, string projectRoot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Parsing JSON configuration file: {FilePath}")]
    private static partial void LogParsingJSONConfigurationFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully parsed JSON configuration with {RootPropertyCount} root properties")]
    private static partial void LogSuccessfullyParsedJSONConfigurationWith(ILogger logger, int rootPropertyCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse JSON configuration file: {FilePath}")]
    private static partial void LogFailedToParseJSONConfiguration(ILogger logger, JsonException exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Parsing YAML configuration file: {FilePath}")]
    private static partial void LogParsingYAMLConfigurationFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully parsed YAML configuration with {RootPropertyCount} root properties")]
    private static partial void LogSuccessfullyParsedYAMLConfigurationWith(ILogger logger, int rootPropertyCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse YAML configuration file: {FilePath}")]
    private static partial void LogFailedToParseYAMLConfiguration(ILogger logger, YamlDotNet.Core.YamlException exception, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {ConfigFileCount} configuration files")]
    private static partial void LogFoundConfigurationFiles(ILogger logger, int configFileCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to validate environment consistency for environment: {Environment}")]
    private static partial void LogFailedToValidateEnvironmentConsistency(ILogger logger, Exception exception, string environment);
}
