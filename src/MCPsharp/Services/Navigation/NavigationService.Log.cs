using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Source-generated logging methods.
/// </summary>
public partial class NavigationService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Error in GoToDefinition for {FilePath}:{Line}:{Column}")]
    private static partial void LogGoToDefinitionError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding overrides for {FilePath}:{Line}:{Column}")]
    private static partial void LogFindOverridesError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding overloads for {FilePath}:{Line}:{Column}")]
    private static partial void LogFindOverloadsError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding base symbol for {FilePath}:{Line}:{Column}")]
    private static partial void LogFindBaseSymbolError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding derived types for {FilePath}:{Line}:{Column}")]
    private static partial void LogFindDerivedTypesError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error going to implementation for {FilePath}:{Line}:{Column}")]
    private static partial void LogGoToImplementationError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error finding all related symbols for {FilePath}:{Line}:{Column}")]
    private static partial void LogFindAllRelatedError(ILogger logger, Exception exception, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to add code context for {FilePath}")]
    private static partial void LogAddCodeContextFailed(ILogger logger, Exception exception, string filePath);
}
