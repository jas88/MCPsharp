using System.Diagnostics;
using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Navigation to method overloads, including extension method overloads.
/// </summary>
public partial class NavigationService
{
    /// <summary>
    /// Find all overloaded versions of a method
    /// </summary>
    public async Task<MultiNavigationResult> FindOverloadsAsync(
        string filePath,
        int line,
        int column,
        bool includeExtensions = false)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column);

            if (resolved == null || resolved.Symbol is not IMethodSymbol method)
            {
                return new MultiNavigationResult
                {
                    Success = false,
                    Message = "No method found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            var locations = new List<NavigationLocation>();

            // Get all methods with the same name in the containing type
            if (method.ContainingType != null)
            {
                var overloads = method.ContainingType.GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary ||
                               m.MethodKind == MethodKind.Constructor);

                foreach (var overload in overloads)
                {
                    var location = CreateNavigationLocation(overload);
                    if (location != null)
                    {
                        // Note: Cannot modify init-only properties after creation
                        // The IsOverride flag is already set correctly by CreateNavigationSymbolInfo
                        locations.Add(location);
                    }
                }
            }

            // Include extension methods if requested
            if (includeExtensions && _workspace.IsInitialized && method.ContainingType != null)
            {
                var extensionMethods = await FindExtensionMethodOverloadsAsync(
                    method.Name, method.ContainingType);

                foreach (var extMethod in extensionMethods)
                {
                    var location = CreateNavigationLocation(extMethod);
                    if (location != null)
                    {
                        locations.Add(location);
                    }
                }
            }

            return new MultiNavigationResult
            {
                Success = true,
                Locations = locations.OrderBy(l => l.Line).ToList(),
                BaseSymbol = CreateNavigationSymbolInfo(method),
                TotalFound = locations.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogFindOverloadsError(_logger, ex, filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding overloads: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<List<IMethodSymbol>> FindExtensionMethodOverloadsAsync(
        string methodName,
        INamedTypeSymbol extendedType)
    {
        var extensionMethods = new List<IMethodSymbol>();

        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync();

            var methodDeclarations = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == methodName);

            foreach (var methodDecl in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
                if (methodSymbol?.IsExtensionMethod == true)
                {
                    // Check if it extends the target type
                    var firstParam = methodSymbol.Parameters.FirstOrDefault();
                    if (firstParam != null)
                    {
                        var paramType = firstParam.Type;
                        if (CrossProjectSymbolComparer.AreEquivalent(paramType, extendedType) ||
                            IsAssignableFrom(extendedType, paramType))
                        {
                            extensionMethods.Add(methodSymbol);
                        }
                    }
                }
            }
        }

        return extensionMethods;
    }

    private static bool IsAssignableFrom(ITypeSymbol targetType, ITypeSymbol sourceType)
    {
        // Check if sourceType can be assigned to targetType
        if (CrossProjectSymbolComparer.AreEquivalent(targetType, sourceType))
        {
            return true;
        }

        // Check inheritance chain
        if (sourceType is INamedTypeSymbol namedSource)
        {
            var current = namedSource.BaseType;
            while (current != null)
            {
                if (CrossProjectSymbolComparer.AreEquivalent(current, targetType))
                {
                    return true;
                }
                current = current.BaseType;
            }

            // Check interfaces
            return namedSource.AllInterfaces.Any(i =>
                CrossProjectSymbolComparer.AreEquivalent(i, targetType));
        }

        return false;
    }
}
