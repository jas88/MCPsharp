using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for finding references and implementations
/// </summary>
public class ReferenceFinderService
{
    private readonly RoslynWorkspace _workspace;

    public ReferenceFinderService(RoslynWorkspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Find all references to a symbol
    /// </summary>
    public async Task<ReferenceResult?> FindReferencesAsync(string? symbolName = null, string? filePath = null, int? line = null, int? column = null)
    {
        ISymbol? symbol = null;

        if (filePath != null && line != null && column != null)
        {
            // Find symbol at location
            var document = _workspace.GetDocument(filePath);
            if (document == null)
            {
                return null;
            }

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (syntaxTree == null || semanticModel == null)
            {
                return null;
            }

            var position = syntaxTree.GetText().Lines[line.Value].Start + column.Value;
            var node = (await syntaxTree.GetRootAsync()).FindToken(position).Parent;
            var symbolInfo = node != null ? semanticModel.GetSymbolInfo(node) : default;
            symbol = symbolInfo.CandidateSymbols.FirstOrDefault();
        }
        else if (symbolName != null)
        {
            // Find symbol by name
            var compilation = _workspace.GetCompilation();
            if (compilation == null)
            {
                return null;
            }

            symbol = compilation.GetSymbolsWithName(n => n == symbolName).FirstOrDefault();
        }

        if (symbol == null)
        {
            return null;
        }

        // Find all references
        var references = new List<Models.Roslyn.ReferenceLocation>();
        var project = _workspace.GetAllDocuments().FirstOrDefault()?.Project;
        if (project == null)
        {
            return null;
        }

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, project.Solution);

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var doc = location.Document;
                var lineSpan = location.Location.GetLineSpan();
                var text = await doc.GetTextAsync();
                var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString();

                references.Add(new Models.Roslyn.ReferenceLocation
                {
                    File = doc.FilePath ?? "",
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    Context = lineText.Trim()
                });
            }
        }

        return new ReferenceResult
        {
            Symbol = symbol.Name,
            References = references,
            TotalReferences = references.Count
        };
    }

    /// <summary>
    /// Find all implementations of an interface or abstract member
    /// </summary>
    public async Task<List<SymbolResult>> FindImplementationsAsync(string symbolName)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return new List<SymbolResult>();
        }

        var symbol = compilation.GetSymbolsWithName(n => n == symbolName, SymbolFilter.Type).FirstOrDefault();
        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return new List<SymbolResult>();
        }

        var results = new List<SymbolResult>();
        var project = _workspace.GetAllDocuments().FirstOrDefault()?.Project;
        if (project == null)
        {
            return results;
        }

        // Find implementations
        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, project.Solution);

        foreach (var impl in implementations.OfType<INamedTypeSymbol>())
        {
            var location = impl.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();
            results.Add(new SymbolResult
            {
                Name = impl.Name,
                Kind = impl.TypeKind.ToString().ToLower(),
                File = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                ContainerName = impl.ContainingNamespace?.ToDisplayString()
            });
        }

        return results;
    }
}
