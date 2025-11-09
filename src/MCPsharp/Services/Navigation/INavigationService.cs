using MCPsharp.Models.Navigation;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Service for code navigation features like go-to-definition, find overrides, etc.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigate to the definition of a symbol at the specified location
    /// </summary>
    Task<NavigationResult> GoToDefinitionAsync(string filePath, int line, int column, bool includeContent = false);

    /// <summary>
    /// Find all overrides of a virtual/abstract member at the specified location
    /// </summary>
    Task<MultiNavigationResult> FindOverridesAsync(string filePath, int line, int column, bool includeInterface = true);

    /// <summary>
    /// Find all overloaded versions of a method at the specified location
    /// </summary>
    Task<MultiNavigationResult> FindOverloadsAsync(string filePath, int line, int column, bool includeExtensions = false);

    /// <summary>
    /// Navigate to the base class or interface definition of a symbol at the specified location
    /// </summary>
    Task<NavigationResult> FindBaseSymbolAsync(string filePath, int line, int column, bool findOriginalDefinition = true);

    /// <summary>
    /// Find all types that derive from or implement the type at the specified location
    /// </summary>
    Task<MultiNavigationResult> FindDerivedTypesAsync(string filePath, int line, int column, bool includeSealedTypes = true, int maxDepth = -1);

    /// <summary>
    /// Navigate to the implementation of an interface member or abstract method
    /// </summary>
    Task<NavigationResult> GoToImplementationAsync(string filePath, int line, int column);

    /// <summary>
    /// Find all symbols (definitions, overrides, implementations) related to the symbol at the specified location
    /// </summary>
    Task<MultiNavigationResult> FindAllRelatedAsync(string filePath, int line, int column);
}