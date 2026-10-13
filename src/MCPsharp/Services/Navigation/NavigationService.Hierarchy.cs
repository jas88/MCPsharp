using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Navigation to derived types and implementations.
/// </summary>
public partial class NavigationService
{
    /// <summary>
    /// Find all types that derive from or implement the type at the specified location
    /// </summary>
    public async Task<MultiNavigationResult> FindDerivedTypesAsync(
        string filePath,
        int line,
        int column,
        bool includeSealedTypes = true,
        int maxDepth = -1)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column);

            if (resolved == null || resolved.Symbol is not INamedTypeSymbol type)
            {
                return new MultiNavigationResult
                {
                    Success = false,
                    Message = "No type found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Check cache first
            var cachedHierarchy = _cache.GetCachedHierarchy(type);
            if (cachedHierarchy?.CachedDerivedTypes != null)
            {
                var cachedLocations = cachedHierarchy.CachedDerivedTypes
                    .Where(t => includeSealedTypes || !t.IsSealed)
                    .Select(CreateNavigationLocation)
                    .Where(l => l != null)
                    .Cast<NavigationLocation>()
                    .ToList();

                return new MultiNavigationResult
                {
                    Success = true,
                    Locations = cachedLocations,
                    BaseSymbol = CreateNavigationSymbolInfo(type),
                    TotalFound = cachedLocations.Count,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Find derived types
            var derivedTypes = await FindDerivedTypesRecursiveAsync(
                type, includeSealedTypes, maxDepth, 0);

            // Cache the results
            if (cachedHierarchy != null)
            {
                cachedHierarchy.CachedDerivedTypes = derivedTypes;
            }
            else
            {
                _cache.CacheSymbolHierarchy(type, new HierarchyInfo
                {
                    Type = type,
                    CachedDerivedTypes = derivedTypes
                });
            }

            var locations = derivedTypes
                .Select(CreateNavigationLocation)
                .Where(l => l != null)
                .Cast<NavigationLocation>()
                .OrderBy(l => l.FilePath)
                .ThenBy(l => l.Line)
                .ToList();

            return new MultiNavigationResult
            {
                Success = true,
                Locations = locations,
                BaseSymbol = CreateNavigationSymbolInfo(type),
                TotalFound = locations.Count,
                HasMore = maxDepth != -1 && derivedTypes.Count >= 100, // Arbitrary limit indicator
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogFindDerivedTypesError(_logger, ex, filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding derived types: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Navigate to the implementation of an interface member or abstract method
    /// </summary>
    public async Task<NavigationResult> GoToImplementationAsync(
        string filePath,
        int line,
        int column)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column);

            if (resolved == null)
            {
                return new NavigationResult
                {
                    Success = false,
                    Message = "No symbol found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            var symbol = resolved.Symbol;

            // Check if symbol is abstract or interface member
            if (!symbol.IsAbstract && symbol.ContainingType?.TypeKind != TypeKind.Interface)
            {
                // If it's already a concrete implementation, return its location
                var location = CreateNavigationLocation(symbol);
                return new NavigationResult
                {
                    Success = true,
                    Location = location,
                    Confidence = SymbolConfidence.Exact,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Find implementations
            if (_workspace.IsInitialized)
            {
                var solution = _workspace.Solution;
                var implementations = await SymbolFinder.FindImplementationsAsync(
                    symbol, solution, cancellationToken: CancellationToken.None);

                // De-duplicate symbols reported once per target framework of a multi-targeted project
                var implList = implementations
                    .DistinctBy(GetSourceLocationKey)
                    .ToList();

                if (implList.Count == 0)
                {
                    return new NavigationResult
                    {
                        Success = false,
                        Message = "No implementations found",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // If single implementation, navigate to it
                if (implList.Count == 1)
                {
                    var location = CreateNavigationLocation(implList[0]);
                    return new NavigationResult
                    {
                        Success = true,
                        Location = location,
                        Confidence = SymbolConfidence.Exact,
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Multiple implementations - return first with alternatives
                var primaryLocation = CreateNavigationLocation(implList[0]);
                var alternatives = implList.Skip(1)
                    .Select(CreateNavigationLocation)
                    .Where(l => l != null)
                    .Cast<NavigationLocation>()
                    .ToList();

                return new NavigationResult
                {
                    Success = true,
                    Location = primaryLocation,
                    Alternatives = alternatives,
                    Confidence = SymbolConfidence.Ambiguous,
                    Message = $"Found {implList.Count} implementations",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            return new NavigationResult
            {
                Success = false,
                Message = "Workspace not available",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogGoToImplementationError(_logger, ex, filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error going to implementation: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<List<INamedTypeSymbol>> FindDerivedTypesRecursiveAsync(
        INamedTypeSymbol baseType,
        bool includeSealedTypes,
        int maxDepth,
        int currentDepth)
    {
        if (maxDepth != -1 && currentDepth >= maxDepth)
        {
            return new List<INamedTypeSymbol>();
        }

        var derivedTypes = new List<INamedTypeSymbol>();
        if (!_workspace.IsInitialized)
        {
            return derivedTypes;
        }

        // Search all types in the compilation
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync();

            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                // Check if it derives from our base type (optionally excluding sealed types)
                if (declaredSymbol is INamedTypeSymbol derivedType &&
                    DerivesFrom(derivedType, baseType) &&
                    (includeSealedTypes || !derivedType.IsSealed))
                {
                    derivedTypes.Add(derivedType);

                    // Recursively find types derived from this one
                    if (maxDepth == -1 || currentDepth + 1 < maxDepth)
                    {
                        var furtherDerived = await FindDerivedTypesRecursiveAsync(
                            derivedType, includeSealedTypes, maxDepth, currentDepth + 1);
                        derivedTypes.AddRange(furtherDerived);
                    }
                }
            }
        }

        return derivedTypes.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>().ToList();
    }

    private static bool DerivesFrom(INamedTypeSymbol derivedType, INamedTypeSymbol baseType)
    {
        // Check direct base type
        if (CrossProjectSymbolComparer.AreEquivalent(derivedType.BaseType, baseType))
        {
            return true;
        }

        // Check interfaces
        if (derivedType.Interfaces.Any(i => CrossProjectSymbolComparer.AreEquivalent(i, baseType)))
        {
            return true;
        }

        // Check base type chain
        var current = derivedType.BaseType;
        while (current != null)
        {
            if (CrossProjectSymbolComparer.AreEquivalent(current, baseType))
            {
                return true;
            }
            current = current.BaseType;
        }

        return false;
    }
}
