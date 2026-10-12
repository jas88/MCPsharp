using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Source-generated logging for <see cref="RenameSymbolService"/>.
/// </summary>
public partial class RenameSymbolService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting rename of '{OldName}' to '{NewName}'")]
    private static partial void LogStartingRename(ILogger logger, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rename validation failed: {Errors}")]
    private static partial void LogRenameValidationFailed(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocking conflicts detected for rename")]
    private static partial void LogBlockingConflicts(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rename completed successfully: {Count} references updated")]
    private static partial void LogRenameCompleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rename operation cancelled")]
    private static partial void LogRenameCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rename operation failed")]
    private static partial void LogRenameFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol found but failed validation: '{SymbolName}'")]
    private static partial void LogSymbolFailedValidation(ILogger logger, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found symbol '{SymbolName}' of kind {SymbolKind} using {Strategy}")]
    private static partial void LogFoundSymbol(ILogger logger, string symbolName, RoslynSymbolKind symbolKind, string? strategy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Found {Count} ambiguous candidates for '{OldName}', selected best match")]
    private static partial void LogAmbiguousCandidates(ILogger logger, int count, string oldName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Symbol '{OldName}' not found with kind {SymbolKind}")]
    private static partial void LogSymbolNotFound(ILogger logger, string oldName, SymbolKind symbolKind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol has no source location, cannot re-resolve")]
    private static partial void LogNoSourceLocation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Document not found for path: {FilePath}")]
    private static partial void LogDocumentNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not get semantic model or syntax root")]
    private static partial void LogNoSemanticModelOrRoot(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not find syntax node at location")]
    private static partial void LogSyntaxNodeNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-resolved symbol '{SymbolName}' (kind: {Kind}) in current compilation via declaration node")]
    private static partial void LogReResolvedViaDeclaration(ILogger logger, string symbolName, RoslynSymbolKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-resolved symbol '{SymbolName}' (kind: {Kind}) in current compilation via SymbolInfo")]
    private static partial void LogReResolvedViaSymbolInfo(ILogger logger, string symbolName, RoslynSymbolKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not re-resolve symbol in current compilation")]
    private static partial void LogCouldNotReResolve(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error re-resolving symbol in current compilation")]
    private static partial void LogReResolveError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid parameters for conflict detection: symbol={HasSymbol}, newName={NewName}")]
    private static partial void LogInvalidConflictParameters(ILogger logger, bool hasSymbol, string newName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detecting conflicts for symbol {SymbolName} (Kind: {Kind}) -> {NewName}")]
    private static partial void LogDetectingConflicts(ILogger logger, string symbolName, RoslynSymbolKind kind, string newName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ContainingSymbol: {ContainingSymbol} (Type: {Type})")]
    private static partial void LogContainingSymbol(ILogger logger, string containingSymbol, string type);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking type member collisions in {TypeName}")]
    private static partial void LogCheckingTypeMemberCollisions(ILogger logger, string typeName);

    // Roslyn symbols format via ToDisplayString(), so the display string is only built when Debug is enabled.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking namespace collisions in {NamespaceName}")]
    private static partial void LogCheckingNamespaceCollisions(ILogger logger, INamespaceSymbol namespaceName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ContainingSymbol is neither a type nor a namespace")]
    private static partial void LogContainingSymbolNeitherTypeNorNamespace(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking inheritance hiding")]
    private static partial void LogCheckingInheritanceHiding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking interface implementation conflicts")]
    private static partial void LogCheckingInterfaceConflicts(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Conflict detection complete: Found {ConflictCount} conflicts for symbol {SymbolName}")]
    private static partial void LogConflictDetectionComplete(ILogger logger, int conflictCount, string symbolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting conflicts for symbol {SymbolName}")]
    private static partial void LogConflictDetectionError(ILogger logger, Exception exception, string? symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CheckTypeMemberCollisionsAsync called: containingType={ContainingType}, newName={NewName}, originalSymbol={OriginalSymbol}")]
    private static partial void LogCheckTypeMemberCollisionsCalled(ILogger logger, string? containingType, string newName, string? originalSymbol);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Early return due to null check: containingType={HasContainingType}, originalSymbol={HasOriginalSymbol}, conflicts={HasConflicts}")]
    private static partial void LogTypeMemberCollisionsEarlyReturn(ILogger logger, bool hasContainingType, bool hasOriginalSymbol, bool hasConflicts);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {MemberCount} members with name '{NewName}' in type {TypeName}")]
    private static partial void LogFoundMembersWithName(ILogger logger, int memberCount, string newName, string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No existing members found with name '{NewName}' in type {TypeName}")]
    private static partial void LogNoExistingMembers(ILogger logger, string newName, string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping null member")]
    private static partial void LogSkippingNullMember(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking member: Name={Name}, Kind={Kind}, IsInSource={IsInSource}")]
    private static partial void LogCheckingMember(ILogger logger, string name, RoslynSymbolKind kind, bool isInSource);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping because it's the same symbol as original")]
    private static partial void LogSkippingSameSymbol(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Existing symbol '{ExistingName}' is not in source code")]
    private static partial void LogExistingSymbolNotInSource(ILogger logger, string existingName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found name collision: '{NewName}' already exists in {TypeName}")]
    private static partial void LogNameCollision(ILogger logger, string newName, string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CheckTypeMemberCollisionsAsync finished with {ConflictCount} conflicts")]
    private static partial void LogCheckTypeMemberCollisionsFinished(ILogger logger, int conflictCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking type member collisions in {TypeName}")]
    private static partial void LogTypeMemberCollisionsError(ILogger logger, Exception exception, string? typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No compilation available for namespace collision checking")]
    private static partial void LogNoCompilationForNamespaceCheck(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No existing symbols found with name '{NewName}' in namespace {NamespaceName}")]
    private static partial void LogNoExistingNamespaceSymbols(ILogger logger, string newName, string namespaceName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found namespace collision: '{NewName}' already exists in namespace {NamespaceName}")]
    private static partial void LogNamespaceCollision(ILogger logger, string newName, string namespaceName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking namespace collisions in {NamespaceName}")]
    private static partial void LogNamespaceCollisionsError(ILogger logger, Exception exception, string? namespaceName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol {SymbolName} is not in a type, skipping inheritance hiding check")]
    private static partial void LogNotInType(ILogger logger, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking inheritance hiding for symbol {SymbolName} -> {NewName}")]
    private static partial void LogCheckingInheritanceHidingFor(ILogger logger, string symbolName, string newName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found inheritance hiding: '{NewName}' hides inherited member from {BaseTypeName}")]
    private static partial void LogInheritanceHidingFound(ILogger logger, string newName, string baseTypeName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking base type {BaseTypeName} for inheritance hiding")]
    private static partial void LogBaseTypeCheckError(ILogger logger, Exception exception, string baseTypeName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking inheritance hiding for symbol {SymbolName}")]
    private static partial void LogInheritanceHidingError(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking interface implementation conflicts for symbol {SymbolName}")]
    private static partial void LogInterfaceConflictsError(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PerformRenameAsync: Renaming symbol '{SymbolName}' of kind {Kind} to '{NewName}'")]
    private static partial void LogPerformRename(ILogger logger, string symbolName, RoslynSymbolKind kind, string newName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using re-resolved symbol '{SymbolName}' from current compilation")]
    private static partial void LogUsingReResolvedSymbol(ILogger logger, string symbolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not re-resolve symbol '{SymbolName}', using original symbol")]
    private static partial void LogUsingOriginalSymbol(ILogger logger, string symbolName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renaming symbol: Name='{Name}', Kind={Kind}, ContainingType={ContainingType}, Locations={LocationCount}")]
    private static partial void LogRenamingSymbolDetails(ILogger logger, string name, RoslynSymbolKind kind, string containingType, int locationCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Solution has {ProjectCount} projects, {DocumentCount} total documents")]
    private static partial void LogSolutionState(ILogger logger, int projectCount, int documentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "SymbolFinder found {ReferenceGroupCount} reference groups")]
    private static partial void LogReferenceGroupCount(ILogger logger, int referenceGroupCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "  Symbol '{SymbolName}' has {LocationCount} reference locations")]
    private static partial void LogReferenceLocationCount(ILogger logger, string symbolName, int locationCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error using SymbolFinder")]
    private static partial void LogSymbolFinderError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rename operation completed. Project changes: {ProjectChangeCount}")]
    private static partial void LogRenameProjectChanges(ILogger logger, int projectChangeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Project '{ProjectName}' has {ChangedDocCount} changed documents")]
    private static partial void LogProjectChangedDocuments(ILogger logger, string projectName, int changedDocCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document '{DocName}' has {ChangeCount} text changes")]
    private static partial void LogDocumentTextChanges(ILogger logger, string docName, int changeCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Text change at span {Span}: '{OldText}' -> '{NewText}'")]
    private static partial void LogTextChange(ILogger logger, TextSpan span, string oldText, string? newText);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol '{SymbolName}' is not in source code")]
    private static partial void LogSymbolNotInSource(ILogger logger, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol '{SymbolName}' is not accessible for rename")]
    private static partial void LogSymbolNotAccessible(ILogger logger, string symbolName);
}
