using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Search strategy for scope-local symbols (local variables, parameters).
/// These symbols are NOT in the compilation symbol table and require
/// document-by-document semantic analysis to find.
/// </summary>
public class ScopeLocalStrategy : ISymbolSearchStrategy
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<ScopeLocalStrategy>? _logger;

    public string StrategyName => "ScopeLocal";

    public ScopeLocalStrategy(RoslynWorkspace workspace, ILogger<ScopeLocalStrategy>? logger = null)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public bool IsApplicableFor(SymbolKind kind)
    {
        // Only applicable for scope-local symbols
        return kind == SymbolKind.Local || kind == SymbolKind.Parameter;
    }

    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request, CancellationToken ct = default)
    {
        var results = new List<ISymbol>();

        try
        {
            // Must search all documents because locals/parameters are scope-local
            var documents = _workspace.GetAllDocuments().ToList();
            _logger?.LogDebug("ScopeLocalStrategy searching {Count} documents for '{Name}'",
                documents.Count, request.Name);

            foreach (var document in documents)
            {
                if (document == null)
                    continue;

                var semanticModel = await _workspace.GetSemanticModelAsync(document, ct);
                var syntaxRoot = await document.GetSyntaxRootAsync(ct);

                if (semanticModel == null || syntaxRoot == null)
                {
                    _logger?.LogDebug("Skipping document {Name} - no semantic model or syntax root",
                        document.Name);
                    continue;
                }

                // Find all identifiers with the matching name
                var identifiers = syntaxRoot.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => string.Equals(id.Identifier.Text, request.Name,
                        StringComparison.OrdinalIgnoreCase));

                foreach (var identifier in identifiers)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(identifier, ct);
                    var symbol = symbolInfo.Symbol;

                    if (symbol == null)
                        continue;

                    // Filter by symbol type
                    bool matches = request.SymbolKind switch
                    {
                        SymbolKind.Local => symbol is ILocalSymbol,
                        SymbolKind.Parameter => symbol is IParameterSymbol,
                        SymbolKind.Any => symbol is ILocalSymbol || symbol is IParameterSymbol,
                        _ => false
                    };

                    if (matches)
                    {
                        results.Add(symbol);
                    }
                }
            }

            // Remove duplicates (same symbol might be found multiple times)
            results = results
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<ISymbol>()
                .ToList();

            _logger?.LogDebug("ScopeLocalStrategy found {Count} candidates for '{Name}'",
                results.Count, request.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in ScopeLocalStrategy for symbol '{Name}'", request.Name);
        }

        return results;
    }
}
