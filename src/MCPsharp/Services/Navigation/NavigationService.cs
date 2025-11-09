using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Implementation of navigation service for code navigation features
/// </summary>
public class NavigationService : INavigationService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ISymbolResolutionService _symbolResolver;
    private readonly INavigationCache _cache;
    private readonly ILogger<NavigationService>? _logger;

    public NavigationService(
        RoslynWorkspace workspace,
        ISymbolResolutionService symbolResolver,
        INavigationCache cache,
        ILogger<NavigationService>? logger = null)
    {
        _workspace = workspace;
        _symbolResolver = symbolResolver;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Navigate to the definition of a symbol at the specified location
    /// </summary>
    public async Task<NavigationResult> GoToDefinitionAsync(
        string filePath,
        int line,
        int column,
        bool includeContent = false)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column,
                new SymbolResolutionOptions { ResolveToOriginalDefinition = true });

            if (resolved == null)
            {
                return new NavigationResult
                {
                    Success = false,
                    Message = "No symbol found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Handle external symbols
            if (!resolved.Symbol.Locations.Any(l => l.IsInSource))
            {
                return CreateExternalSymbolResult(resolved.Symbol, stopwatch.ElapsedMilliseconds);
            }

            // Get primary location for the definition
            var location = resolved.DeclarationLocation;

            // Add code context if requested
            if (includeContent)
            {
                location = await AddCodeContextAsync(location);
            }

            // Get alternatives if ambiguous
            List<NavigationLocation>? alternatives = null;
            if (resolved.Confidence == SymbolConfidence.Ambiguous && resolved.AlternativeSymbols != null)
            {
                alternatives = await GetAlternativeLocationsAsync(resolved.AlternativeSymbols);
            }

            return new NavigationResult
            {
                Success = true,
                Location = location,
                Confidence = resolved.Confidence,
                Alternatives = alternatives,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in GoToDefinition for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error navigating to definition: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

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
            if (!symbol.IsVirtual && !symbol.IsAbstract && !symbol.IsOverride)
            {
                // Check if it's an interface member
                if (symbol.ContainingType?.TypeKind != TypeKind.Interface)
                {
                    return new MultiNavigationResult
                    {
                        Success = false,
                        Message = "Symbol is not virtual, abstract, or an interface member",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Get the original virtual/abstract definition
            ISymbol? originalSymbol = null;

            // Find all overrides
            if (_workspace.IsInitialized)
            {
                var solution = _workspace.Solution;

                originalSymbol = GetOriginalVirtualSymbol(symbol);

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
            _logger?.LogError(ex, "Error finding overrides for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding overrides: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

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
            if (includeExtensions)
            {
                var compilation = _workspace.GetCompilation();
                if (compilation != null && method.ContainingType != null)
                {
                    var extensionMethods = await FindExtensionMethodOverloadsAsync(
                        compilation, method.Name, method.ContainingType);

                    foreach (var extMethod in extensionMethods)
                    {
                        var location = CreateNavigationLocation(extMethod);
                        if (location != null)
                        {
                            locations.Add(location);
                        }
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
            _logger?.LogError(ex, "Error finding overloads for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding overloads: {ex.Message}",
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
            _logger?.LogError(ex, "Error finding base symbol for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error finding base symbol: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

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
            _logger?.LogError(ex, "Error finding derived types for {FilePath}:{Line}:{Column}",
                filePath, line, column);

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

                var implList = implementations.ToList();

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
            _logger?.LogError(ex, "Error going to implementation for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error going to implementation: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Find all related symbols (definitions, overrides, implementations)
    /// </summary>
    public async Task<MultiNavigationResult> FindAllRelatedAsync(
        string filePath,
        int line,
        int column)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var locations = new HashSet<NavigationLocation>(new NavigationLocationComparer());

            // Get definition
            var definition = await GoToDefinitionAsync(filePath, line, column);
            if (definition.Success && definition.Location != null)
            {
                locations.Add(definition.Location);
            }

            // Get overrides
            var overrides = await FindOverridesAsync(filePath, line, column);
            if (overrides.Success)
            {
                foreach (var loc in overrides.Locations)
                {
                    locations.Add(loc);
                }
            }

            // Get base symbol
            var baseSymbol = await FindBaseSymbolAsync(filePath, line, column);
            if (baseSymbol.Success && baseSymbol.Location != null)
            {
                locations.Add(baseSymbol.Location);
            }

            // Get implementations
            var implementation = await GoToImplementationAsync(filePath, line, column);
            if (implementation.Success && implementation.Location != null)
            {
                locations.Add(implementation.Location);
                if (implementation.Alternatives != null)
                {
                    foreach (var alt in implementation.Alternatives)
                    {
                        locations.Add(alt);
                    }
                }
            }

            return new MultiNavigationResult
            {
                Success = true,
                Locations = locations.OrderBy(l => l.FilePath).ThenBy(l => l.Line).ToList(),
                TotalFound = locations.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error finding all related symbols for {FilePath}:{Line}:{Column}",
                filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding related symbols: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Helper methods

    private NavigationResult CreateExternalSymbolResult(ISymbol symbol, long elapsedMs)
    {
        return new NavigationResult
        {
            Success = false,
            IsExternal = true,
            ExternalInfo = new ExternalSymbolInfo
            {
                AssemblyName = symbol.ContainingAssembly?.Name,
                TypeName = symbol.ContainingType?.ToDisplayString(),
                MemberName = symbol.Name,
                Documentation = GetDocumentationSummary(symbol),
                CanDecompile = false // Could check if decompiler is available
            },
            Message = $"Symbol '{symbol.Name}' is defined in external assembly '{symbol.ContainingAssembly?.Name}'",
            ExecutionTimeMs = elapsedMs
        };
    }

    private NavigationLocation? CreateNavigationLocation(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new NavigationLocation
        {
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Symbol = CreateNavigationSymbolInfo(symbol),
            IsPartial = symbol.Locations.Count(l => l.IsInSource) > 1
        };
    }

    private NavigationSymbolInfo CreateNavigationSymbolInfo(ISymbol symbol)
    {
        // Add method-specific information
        if (symbol is IMethodSymbol method)
        {
            return new NavigationSymbolInfo
            {
                Name = symbol.Name,
                Kind = GetSymbolKind(symbol),
                Signature = symbol.ToDisplayString(),
                ContainingType = symbol.ContainingType?.Name,
                Namespace = symbol.ContainingNamespace?.ToDisplayString(),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLower(),
                Documentation = GetDocumentationSummary(symbol),
                IsAbstract = symbol.IsAbstract,
                IsVirtual = symbol.IsVirtual,
                IsOverride = symbol.IsOverride,
                IsSealed = symbol.IsSealed,
                IsStatic = symbol.IsStatic,
                ReturnType = method.ReturnType.ToDisplayString(),
                Parameters = method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}").ToList(),
                IsExplicitInterfaceImplementation = method.ExplicitInterfaceImplementations.Any()
            };
        }

        return new NavigationSymbolInfo
        {
            Name = symbol.Name,
            Kind = GetSymbolKind(symbol),
            Signature = symbol.ToDisplayString(),
            ContainingType = symbol.ContainingType?.Name,
            Namespace = symbol.ContainingNamespace?.ToDisplayString(),
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLower(),
            Documentation = GetDocumentationSummary(symbol),
            IsAbstract = symbol.IsAbstract,
            IsVirtual = symbol.IsVirtual,
            IsOverride = symbol.IsOverride,
            IsSealed = symbol.IsSealed,
            IsStatic = symbol.IsStatic
        };
    }

    private string GetSymbolKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => type.TypeKind.ToString().ToLower(),
            IMethodSymbol => "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLower()
        };
    }

    private string? GetDocumentationSummary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var summaryStart = xml.IndexOf("<summary>");
        var summaryEnd = xml.IndexOf("</summary>");
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            return xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9).Trim();
        }

        return null;
    }

    private ISymbol? GetOriginalVirtualSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetOriginalVirtualMethod(method),
            IPropertySymbol property => GetOriginalVirtualProperty(property),
            IEventSymbol eventSymbol => GetOriginalVirtualEvent(eventSymbol),
            _ => symbol
        };
    }

    private IMethodSymbol GetOriginalVirtualMethod(IMethodSymbol method)
    {
        while (method.OverriddenMethod != null)
        {
            method = method.OverriddenMethod;
        }
        return method;
    }

    private IPropertySymbol GetOriginalVirtualProperty(IPropertySymbol property)
    {
        while (property.OverriddenProperty != null)
        {
            property = property.OverriddenProperty;
        }
        return property;
    }

    private IEventSymbol GetOriginalVirtualEvent(IEventSymbol eventSymbol)
    {
        while (eventSymbol.OverriddenEvent != null)
        {
            eventSymbol = eventSymbol.OverriddenEvent;
        }
        return eventSymbol;
    }

    private async Task<NavigationLocation> AddCodeContextAsync(NavigationLocation location)
    {
        try
        {
            var document = _workspace.GetDocument(location.FilePath);
            if (document == null)
            {
                return location;
            }

            var text = await document.GetTextAsync();
            var lines = text.Lines;

            if (location.Line >= 0 && location.Line < lines.Count)
            {
                var targetLine = lines[location.Line];
                var before = new List<string>();
                var after = new List<string>();

                // Get 2 lines before
                for (int i = Math.Max(0, location.Line - 2); i < location.Line; i++)
                {
                    before.Add(lines[i].ToString());
                }

                // Get 2 lines after
                for (int i = location.Line + 1; i <= Math.Min(lines.Count - 1, location.Line + 2); i++)
                {
                    after.Add(lines[i].ToString());
                }

                // Create new location with context
                return new NavigationLocation
                {
                    FilePath = location.FilePath,
                    Line = location.Line,
                    Column = location.Column,
                    Symbol = location.Symbol,
                    IsPartial = location.IsPartial,
                    IsPrimary = location.IsPrimary,
                    Context = new CodeContext
                    {
                        Before = before,
                        Target = targetLine.ToString(),
                        After = after,
                        StartLine = Math.Max(0, location.Line - 2),
                        EndLine = Math.Min(lines.Count - 1, location.Line + 2)
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to add code context for {FilePath}", location.FilePath);
        }

        return location;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<List<NavigationLocation>> GetAlternativeLocationsAsync(
        IEnumerable<ISymbol> symbols)
    {
        var locations = new List<NavigationLocation>();

        foreach (var symbol in symbols)
        {
            var location = CreateNavigationLocation(symbol);
            if (location != null)
            {
                locations.Add(location);
            }
        }

        return locations;
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
        var compilation = _workspace.GetCompilation();

        if (compilation == null)
        {
            return derivedTypes;
        }

        // Search all types in the compilation
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync();

            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (declaredSymbol is INamedTypeSymbol derivedType)
                {
                    // Check if it derives from our base type
                    if (DerivesFrom(derivedType, baseType))
                    {
                        if (includeSealedTypes || !derivedType.IsSealed)
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
            }
        }

        return derivedTypes.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>().ToList();
    }

    private bool DerivesFrom(INamedTypeSymbol derivedType, INamedTypeSymbol baseType)
    {
        // Check direct base type
        if (SymbolEqualityComparer.Default.Equals(derivedType.BaseType, baseType))
        {
            return true;
        }

        // Check interfaces
        if (derivedType.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType)))
        {
            return true;
        }

        // Check base type chain
        var current = derivedType.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
            current = current.BaseType;
        }

        return false;
    }

    private async Task<List<IMethodSymbol>> FindExtensionMethodOverloadsAsync(
        Compilation compilation,
        string methodName,
        INamedTypeSymbol extendedType)
    {
        var extensionMethods = new List<IMethodSymbol>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
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
                        if (SymbolEqualityComparer.Default.Equals(paramType, extendedType) ||
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

    private bool IsAssignableFrom(ITypeSymbol targetType, ITypeSymbol sourceType)
    {
        // Check if sourceType can be assigned to targetType
        if (SymbolEqualityComparer.Default.Equals(targetType, sourceType))
        {
            return true;
        }

        // Check inheritance chain
        if (sourceType is INamedTypeSymbol namedSource)
        {
            var current = namedSource.BaseType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, targetType))
                {
                    return true;
                }
                current = current.BaseType;
            }

            // Check interfaces
            return namedSource.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i, targetType));
        }

        return false;
    }

    /// <summary>
    /// Comparer for navigation locations to avoid duplicates
    /// </summary>
    private class NavigationLocationComparer : IEqualityComparer<NavigationLocation>
    {
        public bool Equals(NavigationLocation? x, NavigationLocation? y)
        {
            if (x == null || y == null) return false;
            return x.FilePath == y.FilePath && x.Line == y.Line && x.Column == y.Column;
        }

        public int GetHashCode(NavigationLocation obj)
        {
            return HashCode.Combine(obj.FilePath, obj.Line, obj.Column);
        }
    }
}