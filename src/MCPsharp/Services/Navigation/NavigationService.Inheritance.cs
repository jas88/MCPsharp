using System.Diagnostics;
using MCPsharp.Models.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Navigation to overriding members and base symbols.
/// </summary>
public partial class NavigationService
{
    /// <summary>
    /// Find all overrides of a virtual/abstract member
    /// </summary>
    public async Task<MultiNavigationResult> FindOverridesAsync(
        string filePath,
        int line,
        int column,
        bool includeInterface = true)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column);

            if (resolved == null)
            {
                return new MultiNavigationResult
                {
                    Success = false,
                    Message = "No symbol found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            var symbol = resolved.Symbol;
            var locations = new List<NavigationLocation>();

            // Check if symbol can be overridden
            // (interface members are allowed even though they are not virtual/abstract)
            if (!symbol.IsVirtual && !symbol.IsAbstract && !symbol.IsOverride &&
                symbol.ContainingType?.TypeKind != TypeKind.Interface)
            {
                return new MultiNavigationResult
                {
                    Success = false,
                    Message = "Symbol is not virtual, abstract, or an interface member",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Get the original virtual/abstract definition
            ISymbol? originalSymbol = null;

            // Find all overrides
            if (_workspace.IsInitialized)
            {
                var solution = _workspace.Solution;

                originalSymbol = GetOriginalVirtualSymbol(symbol);
                if (originalSymbol == null)
                    return new MultiNavigationResult { Locations = locations };

                // Find overrides using Roslyn's SymbolFinder
                var overrides = await SymbolFinder.FindOverridesAsync(
                    originalSymbol, solution, cancellationToken: CancellationToken.None);

                foreach (var overrideSymbol in overrides)
                {
                    var overrideLocation = CreateNavigationLocation(overrideSymbol);
                    if (overrideLocation != null)
                    {
                        locations.Add(overrideLocation);
                    }
                }

                // Include interface implementations if requested
                if (includeInterface && symbol.ContainingType?.TypeKind == TypeKind.Interface)
                {
                    var implementations = await SymbolFinder.FindImplementationsAsync(
                        symbol, solution, cancellationToken: CancellationToken.None);

                    foreach (var impl in implementations)
                    {
                        var implLocation = CreateNavigationLocation(impl);
                        if (implLocation != null && !locations.Any(l =>
                            l.FilePath == implLocation.FilePath &&
                            l.Line == implLocation.Line))
                        {
                            locations.Add(implLocation);
                        }
                    }
                }
            }

            // Multi-targeted projects yield the same source location once per target framework
            locations = locations.Distinct(new NavigationLocationComparer()).ToList();

            return new MultiNavigationResult
            {
                Success = true,
                Locations = locations.OrderBy(l => l.FilePath).ThenBy(l => l.Line).ToList(),
                BaseSymbol = CreateNavigationSymbolInfo(originalSymbol ?? symbol),
                TotalFound = locations.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogFindOverridesError(_logger, ex, filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding overrides: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Navigate to the base class or interface definition
    /// </summary>
    public async Task<NavigationResult> FindBaseSymbolAsync(
        string filePath,
        int line,
        int column,
        bool findOriginalDefinition = true)
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

            ISymbol? baseSymbol = null;

            // Handle different symbol types
            switch (resolved.Symbol)
            {
                case INamedTypeSymbol type:
                    baseSymbol = type.BaseType;
                    break;

                case IMethodSymbol method:
                    if (method.IsOverride)
                    {
                        baseSymbol = method.OverriddenMethod;
                        if (findOriginalDefinition && baseSymbol != null)
                        {
                            // Find the original virtual/abstract definition
                            baseSymbol = GetOriginalVirtualSymbol(baseSymbol);
                        }
                    }
                    else if (method.ExplicitInterfaceImplementations.Any())
                    {
                        baseSymbol = method.ExplicitInterfaceImplementations.First();
                    }
                    break;

                case IPropertySymbol property:
                    if (property.IsOverride)
                    {
                        baseSymbol = property.OverriddenProperty;
                        if (findOriginalDefinition && baseSymbol != null)
                        {
                            baseSymbol = GetOriginalVirtualSymbol(baseSymbol);
                        }
                    }
                    else if (property.ExplicitInterfaceImplementations.Any())
                    {
                        baseSymbol = property.ExplicitInterfaceImplementations.First();
                    }
                    break;

                case IEventSymbol eventSymbol:
                    if (eventSymbol.IsOverride)
                    {
                        baseSymbol = eventSymbol.OverriddenEvent;
                        if (findOriginalDefinition && baseSymbol != null)
                        {
                            baseSymbol = GetOriginalVirtualSymbol(baseSymbol);
                        }
                    }
                    else if (eventSymbol.ExplicitInterfaceImplementations.Any())
                    {
                        baseSymbol = eventSymbol.ExplicitInterfaceImplementations.First();
                    }
                    break;
            }

            if (baseSymbol == null)
            {
                return new NavigationResult
                {
                    Success = false,
                    Message = "No base symbol found for the specified symbol",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Handle external symbols
            if (!baseSymbol.Locations.Any(l => l.IsInSource))
            {
                return CreateExternalSymbolResult(baseSymbol, stopwatch.ElapsedMilliseconds);
            }

            var location = CreateNavigationLocation(baseSymbol);

            return new NavigationResult
            {
                Success = true,
                Location = location,
                Confidence = SymbolConfidence.Exact,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogFindBaseSymbolError(_logger, ex, filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error finding base symbol: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static ISymbol? GetOriginalVirtualSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetOriginalVirtualMethod(method),
            IPropertySymbol property => GetOriginalVirtualProperty(property),
            IEventSymbol eventSymbol => GetOriginalVirtualEvent(eventSymbol),
            _ => symbol
        };
    }

    private static IMethodSymbol GetOriginalVirtualMethod(IMethodSymbol method)
    {
        while (method.OverriddenMethod != null)
        {
            method = method.OverriddenMethod;
        }
        return method;
    }

    private static IPropertySymbol GetOriginalVirtualProperty(IPropertySymbol property)
    {
        while (property.OverriddenProperty != null)
        {
            property = property.OverriddenProperty;
        }
        return property;
    }

    private static IEventSymbol GetOriginalVirtualEvent(IEventSymbol eventSymbol)
    {
        while (eventSymbol.OverriddenEvent != null)
        {
            eventSymbol = eventSymbol.OverriddenEvent;
        }
        return eventSymbol;
    }
}
