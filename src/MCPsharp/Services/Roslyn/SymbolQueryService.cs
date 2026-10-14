using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for querying symbols in the workspace
/// </summary>
public class SymbolQueryService
{
    private readonly RoslynWorkspace _workspace;

    public SymbolQueryService(RoslynWorkspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Find ISymbol objects by name and optional kind filter (for rename operations)
    /// </summary>
    public List<ISymbol> FindSymbolsByName(string name, string? kind = null)
    {
        var symbols = _workspace.FindSourceSymbols(name, SymbolFilter.TypeAndMember);
        var results = new List<ISymbol>();

        foreach (var symbol in symbols)
        {
            // Filter by kind if specified
            if (kind != null && !MatchesKind(symbol, kind))
            {
                continue;
            }

            var location = symbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                continue;
            }

            results.Add(symbol);
        }

        return results;
    }

    /// <summary>
    /// Find symbols by name and optional kind filter
    /// </summary>
    public Task<List<SymbolResult>> FindSymbolsAsync(string name, string? kind = null)
    {
        return Task.FromResult(FindSymbols(name, kind));
    }

    private List<SymbolResult> FindSymbols(string name, string? kind)
    {
        var symbols = _workspace.FindSourceSymbols(name, SymbolFilter.TypeAndMember);
        var results = new List<SymbolResult>();

        foreach (var symbol in symbols)
        {
            // Filter by kind if specified
            if (kind != null && !MatchesKind(symbol, kind))
            {
                continue;
            }

            var location = symbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();

            results.Add(new SymbolResult
            {
                Name = symbol.Name,
                Kind = GetSymbolKindString(symbol),
                File = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                ContainerName = symbol.ContainingNamespace?.ToDisplayString()
            });
        }

        return results;
    }

    /// <summary>
    /// Get detailed symbol information at a specific location
    /// </summary>
    public async Task<Models.Roslyn.SymbolInfo?> GetSymbolAtLocationAsync(string filePath, int line, int column)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
        {
            return null;
        }

        var syntaxTree = await document.GetSyntaxTreeAsync();
        var semanticModel = await _workspace.GetSemanticModelAsync(document);
        if (syntaxTree == null || semanticModel == null)
        {
            return null;
        }

        var position = (await syntaxTree.GetTextAsync()).Lines[line].Start + column;
        var node = (await syntaxTree.GetRootAsync()).FindToken(position).Parent;

        if (node == null)
        {
            return null;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = semanticModel.GetDeclaredSymbol(node) ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol == null)
        {
            return null;
        }

        return ConvertToSymbolInfo(symbol);
    }

    private static Models.Roslyn.SymbolInfo ConvertToSymbolInfo(ISymbol symbol)
    {
        var members = new List<MemberInfo>();

        if (symbol is INamedTypeSymbol typeSymbol)
        {
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member.Kind == Microsoft.CodeAnalysis.SymbolKind.Method &&
                    member is IMethodSymbol method &&
                    method.MethodKind != MethodKind.Ordinary &&
                    method.MethodKind != MethodKind.Constructor)
                {
                    continue;
                }

                var memberLocation = member.Locations.FirstOrDefault();
                var memberLine = memberLocation?.GetLineSpan().StartLinePosition.Line ?? 0;

                members.Add(new MemberInfo
                {
                    Name = member.Name,
                    Kind = member.Kind.ToString().ToLowerInvariant(),
                    Type = GetMemberType(member),
                    Line = memberLine,
                    Accessibility = member.DeclaredAccessibility.ToString().ToLowerInvariant()
                });
            }
        }

        return new Models.Roslyn.SymbolInfo
        {
            Name = symbol.Name,
            Kind = GetSymbolKindString(symbol),
            Signature = symbol.ToDisplayString(),
            Documentation = GetDocumentation(symbol),
            Namespace = symbol.ContainingNamespace?.ToDisplayString(),
            BaseTypes = GetBaseTypes(symbol),
            Members = members,
            Symbol = symbol  // Add the raw Roslyn symbol
        };
    }

    private static bool MatchesKind(ISymbol symbol, string kind)
    {
        return string.Equals(GetSymbolKindString(symbol), kind, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSymbolKindString(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Class } => "class",
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
            IPropertySymbol => "property",
            IMethodSymbol => "method",
            IFieldSymbol => "field",
            INamespaceSymbol => "namespace",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };
    }

    private static string GetMemberType(ISymbol member)
    {
        return member switch
        {
            IPropertySymbol prop => prop.Type.ToDisplayString(),
            IMethodSymbol method => method.ReturnType.ToDisplayString(),
            IFieldSymbol field => field.Type.ToDisplayString(),
            _ => "unknown"
        };
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        // Simple extraction of summary
        var summaryStart = xml.IndexOf("<summary>", StringComparison.Ordinal);
        var summaryEnd = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            return xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9).Trim();
        }

        return null;
    }

    private static List<string> GetBaseTypes(ISymbol symbol)
    {
        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return new List<string>();
        }

        var baseTypes = new List<string>();
        if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(typeSymbol.BaseType.ToDisplayString());
        }

        baseTypes.AddRange(typeSymbol.Interfaces.Select(i => i.ToDisplayString()));
        return baseTypes;
    }

    /// <summary>
    /// Get all types in the project
    /// </summary>
    public async Task<List<SymbolResult>> GetAllTypesAsync()
    {
        var results = new List<SymbolResult>();

        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
            {
                continue;
            }

            var root = await syntaxTree.GetRootAsync();

            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (symbol != null)
                {
                    var location = symbol.Locations.FirstOrDefault();
                    if (location != null && location.IsInSource)
                    {
                        var lineSpan = location.GetLineSpan();
                        results.Add(new SymbolResult
                        {
                            Name = symbol.Name,
                            Kind = GetSymbolKindString(symbol),
                            File = lineSpan.Path,
                            Line = lineSpan.StartLinePosition.Line,
                            Column = lineSpan.StartLinePosition.Character,
                            ContainerName = symbol.ContainingNamespace?.ToDisplayString()
                        });
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Get documentation for a symbol
    /// </summary>
    public static Task<string?> GetSymbolDocumentationAsync(ISymbol symbol)
    {
        return Task.FromResult(GetDocumentation(symbol));
    }

    /// <summary>
    /// Get symbols in a specific namespace
    /// </summary>
    public Task<List<SymbolResult>> GetSymbolsInNamespaceAsync(string namespaceName)
    {
        return Task.FromResult(GetSymbolsInNamespace(namespaceName));
    }

    private List<SymbolResult> GetSymbolsInNamespace(string namespaceName)
    {
        var results = new List<SymbolResult>();
        var seenLocations = new HashSet<(string, int, int)>();

        // A namespace can be declared in several projects; collect its members from each of them
        var simpleName = namespaceName.Split('.')[^1];
        var namespaceSymbols = _workspace.FindSourceSymbols(simpleName, SymbolFilter.Namespace)
            .OfType<INamespaceSymbol>()
            .Where(ns => ns.ToDisplayString() == namespaceName);

        foreach (var namespaceSymbol in namespaceSymbols)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                var location = member.Locations.FirstOrDefault();
                if (location != null && location.IsInSource &&
                    seenLocations.Add((location.GetLineSpan().Path, location.SourceSpan.Start, location.SourceSpan.Length)))
                {
                    var lineSpan = location.GetLineSpan();
                    results.Add(new SymbolResult
                    {
                        Name = member.Name,
                        Kind = GetSymbolKindString(member),
                        File = lineSpan.Path,
                        Line = lineSpan.StartLinePosition.Line,
                        Column = lineSpan.StartLinePosition.Character,
                        ContainerName = member.ContainingNamespace?.ToDisplayString()
                    });
                }
            }
        }

        return results;
    }
}
