using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Roslyn;

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
    /// Find symbols by name and optional kind filter
    /// </summary>
    public async Task<List<SymbolResult>> FindSymbolsAsync(string name, string? kind = null)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return new List<SymbolResult>();
        }

        var symbols = compilation.GetSymbolsWithName(n => n == name, SymbolFilter.TypeAndMember);
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

        var position = syntaxTree.GetText().Lines[line].Start + column;
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

        return ConvertToSymbolInfo(symbol, document);
    }

    private Models.Roslyn.SymbolInfo ConvertToSymbolInfo(ISymbol symbol, Document document)
    {
        var members = new List<MemberInfo>();

        if (symbol is INamedTypeSymbol typeSymbol)
        {
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member.Kind == SymbolKind.Method && member is IMethodSymbol method)
                {
                    if (method.MethodKind != MethodKind.Ordinary && method.MethodKind != MethodKind.Constructor)
                    {
                        continue;
                    }
                }

                var memberLocation = member.Locations.FirstOrDefault();
                var memberLine = memberLocation?.GetLineSpan().StartLinePosition.Line ?? 0;

                members.Add(new MemberInfo
                {
                    Name = member.Name,
                    Kind = member.Kind.ToString().ToLower(),
                    Type = GetMemberType(member),
                    Line = memberLine,
                    Accessibility = member.DeclaredAccessibility.ToString().ToLower()
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

    private bool MatchesKind(ISymbol symbol, string kind)
    {
        var symbolKind = GetSymbolKindString(symbol).ToLower();
        return symbolKind == kind.ToLower();
    }

    private string GetSymbolKindString(ISymbol symbol)
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
            _ => symbol.Kind.ToString().ToLower()
        };
    }

    private string GetMemberType(ISymbol member)
    {
        return member switch
        {
            IPropertySymbol prop => prop.Type.ToDisplayString(),
            IMethodSymbol method => method.ReturnType.ToDisplayString(),
            IFieldSymbol field => field.Type.ToDisplayString(),
            _ => "unknown"
        };
    }

    private string? GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        // Simple extraction of summary
        var summaryStart = xml.IndexOf("<summary>");
        var summaryEnd = xml.IndexOf("</summary>");
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            return xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9).Trim();
        }

        return null;
    }

    private List<string> GetBaseTypes(ISymbol symbol)
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
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return new List<SymbolResult>();
        }

        var results = new List<SymbolResult>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
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
    public Task<string?> GetSymbolDocumentationAsync(ISymbol symbol)
    {
        return Task.FromResult(GetDocumentation(symbol));
    }

    /// <summary>
    /// Get symbols in a specific namespace
    /// </summary>
    public async Task<List<SymbolResult>> GetSymbolsInNamespaceAsync(string namespaceName)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return new List<SymbolResult>();
        }

        var results = new List<SymbolResult>();

        // Find the namespace symbol
        var namespaceSymbol = compilation.GetSymbolsWithName(
            n => n == namespaceName.Split('.').Last(),
            SymbolFilter.Namespace)
            .OfType<INamespaceSymbol>()
            .FirstOrDefault(ns => ns.ToDisplayString() == namespaceName);

        if (namespaceSymbol != null)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                var location = member.Locations.FirstOrDefault();
                if (location != null && location.IsInSource)
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
