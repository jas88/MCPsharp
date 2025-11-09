using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Service responsible for resolving symbols from positions in source code
/// </summary>
public class SymbolResolutionService : ISymbolResolutionService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<SymbolResolutionService>? _logger;

    public SymbolResolutionService(
        RoslynWorkspace workspace,
        ILogger<SymbolResolutionService>? logger = null)
    {
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a symbol at a specific position in a file
    /// </summary>
    public async Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string filePath,
        int line,
        int column,
        SymbolResolutionOptions? options = null)
    {
        options ??= new SymbolResolutionOptions();

        try
        {
            // Get document and semantic model
            var document = _workspace.GetDocument(filePath);
            if (document == null)
            {
                _logger?.LogWarning("Document not found: {FilePath}", filePath);
                return null;
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (syntaxTree == null || semanticModel == null)
            {
                _logger?.LogWarning("Could not get syntax tree or semantic model for: {FilePath}", filePath);
                return null;
            }

            // Find position in the text
            var text = await syntaxTree.GetTextAsync();
            var position = GetTextPosition(text, line, column);
            if (position == -1)
            {
                _logger?.LogWarning("Invalid position: line {Line}, column {Column} in {FilePath}", line, column, filePath);
                return null;
            }

            // Find token at position
            var root = await syntaxTree.GetRootAsync();
            var token = root.FindToken(position);

            // Try multiple resolution strategies
            var (symbol, confidence, alternatives) = await ResolveSymbolWithStrategiesAsync(
                token, position, semanticModel, options);

            if (symbol == null)
            {
                _logger?.LogDebug("No symbol found at position {Line}:{Column} in {FilePath}", line, column, filePath);
                return null;
            }

            // Get declaration location
            var declarationLocation = GetDeclarationLocation(symbol, options);
            if (declarationLocation == null)
            {
                _logger?.LogDebug("Symbol {Symbol} has no source location", symbol.Name);
                return null;
            }

            // Get all locations for partial symbols
            var allLocations = GetAllSymbolLocations(symbol);

            return new ResolvedSymbol
            {
                Symbol = symbol,
                DeclarationLocation = declarationLocation,
                Confidence = confidence,
                AlternativeSymbols = alternatives,
                AllLocations = allLocations
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving symbol at {FilePath}:{Line}:{Column}", filePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Resolve a symbol by name with optional context
    /// </summary>
    public async Task<ISymbol?> ResolveSymbolByNameAsync(
        string symbolName,
        string? containingType = null,
        string? containingNamespace = null)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return null;
        }

        // Find all symbols with the given name
        var symbols = compilation.GetSymbolsWithName(
            name => name == symbolName,
            SymbolFilter.All,
            CancellationToken.None);

        // Filter by context if provided
        foreach (var symbol in symbols)
        {
            if (containingType != null)
            {
                if (symbol.ContainingType?.Name != containingType &&
                    symbol.ContainingType?.ToDisplayString() != containingType)
                {
                    continue;
                }
            }

            if (containingNamespace != null)
            {
                if (symbol.ContainingNamespace?.ToDisplayString() != containingNamespace)
                {
                    continue;
                }
            }

            return symbol;
        }

        return null;
    }

    private async Task<(ISymbol? symbol, SymbolConfidence confidence, List<ISymbol>? alternatives)>
        ResolveSymbolWithStrategiesAsync(
            SyntaxToken token,
            int position,
            SemanticModel semanticModel,
            SymbolResolutionOptions options)
    {
        ISymbol? symbol = null;
        var confidence = SymbolConfidence.Exact;
        var alternatives = new List<ISymbol>();

        // Strategy 1: Try to get symbol directly from the token's parent
        var node = token.Parent;
        if (node != null)
        {
            // Check if it's a declaration
            symbol = semanticModel.GetDeclaredSymbol(node);

            // If not a declaration, try to get symbol info
            if (symbol == null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);

                if (symbolInfo.Symbol != null)
                {
                    symbol = symbolInfo.Symbol;
                }
                else if (symbolInfo.CandidateSymbols.Length > 0)
                {
                    // Multiple candidates - ambiguous
                    symbol = SelectBestCandidate(symbolInfo.CandidateSymbols, node, options);
                    alternatives.AddRange(symbolInfo.CandidateSymbols.Where(s => s != symbol));
                    confidence = symbolInfo.CandidateSymbols.Length == 1
                        ? SymbolConfidence.Exact
                        : SymbolConfidence.Ambiguous;
                }
            }
        }

        // Strategy 2: Check if it's a type reference
        if (symbol == null && node is TypeSyntax typeSyntax)
        {
            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            if (typeInfo.Type != null)
            {
                symbol = typeInfo.Type;
            }
        }

        // Strategy 3: Check if it's an identifier that's part of a member access
        if (symbol == null && token.IsKind(SyntaxKind.IdentifierToken))
        {
            // Walk up the tree to find a meaningful node
            var current = token.Parent;
            while (current != null && symbol == null)
            {
                switch (current)
                {
                    case MemberAccessExpressionSyntax memberAccess:
                        var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                        symbol = memberSymbolInfo.Symbol ?? memberSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;

                    case InvocationExpressionSyntax invocation:
                        var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocation);
                        symbol = invocationSymbolInfo.Symbol ?? invocationSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;

                    case IdentifierNameSyntax identifier:
                        var identifierSymbolInfo = semanticModel.GetSymbolInfo(identifier);
                        symbol = identifierSymbolInfo.Symbol ?? identifierSymbolInfo.CandidateSymbols.FirstOrDefault();
                        break;
                }

                current = current.Parent;
            }

            if (symbol != null)
            {
                confidence = SymbolConfidence.High;
            }
        }

        // Strategy 4: Try to find symbol at position using Roslyn's FindSymbols
        if (symbol == null && _workspace.IsInitialized)
        {
            var solution = _workspace.Solution;
            var document = solution.GetDocument(semanticModel.SyntaxTree);
            if (document != null)
            {
                var symbolAtPosition = await SymbolFinder.FindSymbolAtPositionAsync(
                    document, position, CancellationToken.None);

                if (symbolAtPosition != null)
                {
                    symbol = symbolAtPosition;
                    confidence = SymbolConfidence.Medium;
                }
            }
        }

        // Get original definition if requested
        if (symbol != null && options.ResolveToOriginalDefinition)
        {
            symbol = symbol.OriginalDefinition;
        }

        return (symbol, confidence, alternatives.Any() ? alternatives : null);
    }

    private ISymbol SelectBestCandidate(
        IEnumerable<ISymbol> candidates,
        SyntaxNode node,
        SymbolResolutionOptions options)
    {
        var candidateList = candidates.ToList();

        // If only one candidate, return it
        if (candidateList.Count == 1)
        {
            return candidateList[0];
        }

        // Prefer implementations over interfaces if requested
        if (options.PreferImplementationOverInterface)
        {
            var implementations = candidateList
                .Where(s => s.Kind != Microsoft.CodeAnalysis.SymbolKind.NamedType ||
                           ((INamedTypeSymbol)s).TypeKind != TypeKind.Interface)
                .ToList();

            if (implementations.Count == 1)
            {
                return implementations[0];
            }
        }

        // Prefer symbols in the current project (not from GAC/framework assemblies)
        var currentProjectSymbols = candidateList
            .Where(s => s.Locations.Any(l => l.IsInSource))
            .ToList();

        if (currentProjectSymbols.Count == 1)
        {
            return currentProjectSymbols[0];
        }

        // Prefer the most derived type
        if (candidateList.All(s => s is INamedTypeSymbol))
        {
            var types = candidateList.Cast<INamedTypeSymbol>().ToList();
            var mostDerived = types.FirstOrDefault(t =>
                types.All(other => !t.AllInterfaces.Contains(other) && t.BaseType != other));

            if (mostDerived != null)
            {
                return mostDerived;
            }
        }

        // Default to first candidate
        return candidateList[0];
    }

    private NavigationLocation? GetDeclarationLocation(ISymbol symbol, SymbolResolutionOptions options)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            // Check if symbol is in metadata and we should include it
            if (options.IncludeMetadata && symbol.Locations.Any(l => l.IsInMetadata))
            {
                // Return a special marker for metadata symbols
                return new NavigationLocation
                {
                    FilePath = $"[metadata] {symbol.ContainingAssembly?.Name ?? "Unknown"}",
                    Line = -1,
                    Column = -1,
                    Symbol = CreateNavigationSymbolInfo(symbol)
                };
            }
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new NavigationLocation
        {
            FilePath = lineSpan.Path,
            Line = lineSpan.StartLinePosition.Line,
            Column = lineSpan.StartLinePosition.Character,
            Symbol = CreateNavigationSymbolInfo(symbol),
            IsPartial = IsPartialSymbol(symbol),
            IsPrimary = IsPrimaryLocation(symbol, location)
        };
    }

    private List<NavigationLocation> GetAllSymbolLocations(ISymbol symbol)
    {
        var locations = new List<NavigationLocation>();

        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var lineSpan = location.GetLineSpan();
            locations.Add(new NavigationLocation
            {
                FilePath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                Symbol = CreateNavigationSymbolInfo(symbol),
                IsPartial = IsPartialSymbol(symbol),
                IsPrimary = IsPrimaryLocation(symbol, location)
            });
        }

        return locations.OrderBy(l => l.FilePath).ThenBy(l => l.Line).ToList();
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
        // Add property-specific information
        else if (symbol is IPropertySymbol property)
        {
            var modifiers = new List<string>();
            if (property.IsReadOnly) modifiers.Add("readonly");
            if (property.IsWriteOnly) modifiers.Add("writeonly");

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
                ReturnType = property.Type.ToDisplayString(),
                Modifiers = modifiers
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
            INamedTypeSymbol type => type.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                TypeKind.Enum => "enum",
                TypeKind.Delegate => "delegate",
                _ => "type"
            },
            IMethodSymbol method => method.MethodKind switch
            {
                MethodKind.Constructor => "constructor",
                MethodKind.Destructor => "destructor",
                MethodKind.UserDefinedOperator => "operator",
                MethodKind.Conversion => "conversion",
                MethodKind.PropertyGet => "getter",
                MethodKind.PropertySet => "setter",
                _ => "method"
            },
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            INamespaceSymbol => "namespace",
            IParameterSymbol => "parameter",
            ILocalSymbol => "local",
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

        // Simple extraction of summary tag content
        var summaryStart = xml.IndexOf("<summary>");
        var summaryEnd = xml.IndexOf("</summary>");
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            var summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
            // Clean up whitespace and remove extra indentation
            return summary.Trim().Replace("\n    ", " ").Replace("\n", " ");
        }

        return null;
    }

    private bool IsPartialSymbol(ISymbol symbol)
    {
        return symbol.Locations.Count(l => l.IsInSource) > 1;
    }

    private bool IsPrimaryLocation(ISymbol symbol, Location location)
    {
        // For partial symbols, the primary location is usually the first one
        // or the one with the most complete definition
        var sourceLocations = symbol.Locations.Where(l => l.IsInSource).ToList();
        if (sourceLocations.Count <= 1)
        {
            return true;
        }

        // For methods, prefer the one with implementation
        if (symbol is IMethodSymbol method)
        {
            // Check if this location has the method body
            // This would require parsing the syntax tree at each location
            // For now, just use the first location as primary
            return sourceLocations[0] == location;
        }

        // Default to first location as primary
        return sourceLocations[0] == location;
    }

    private int GetTextPosition(Microsoft.CodeAnalysis.Text.SourceText text, int line, int column)
    {
        if (line < 0 || line >= text.Lines.Count)
        {
            return -1;
        }

        var textLine = text.Lines[line];
        if (column < 0 || column > textLine.Span.Length)
        {
            return -1;
        }

        return textLine.Start + column;
    }
}

/// <summary>
/// Interface for symbol resolution service
/// </summary>
public interface ISymbolResolutionService
{
    Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string filePath,
        int line,
        int column,
        SymbolResolutionOptions? options = null);

    Task<ISymbol?> ResolveSymbolByNameAsync(
        string symbolName,
        string? containingType = null,
        string? containingNamespace = null);
}