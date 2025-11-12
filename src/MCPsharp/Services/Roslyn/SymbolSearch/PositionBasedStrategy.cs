using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Search strategy that finds symbols at a specific position in a file.
/// This is the most precise strategy when location information is available.
/// Uses Roslyn's SymbolFinder.FindSymbolAtPositionAsync().
/// </summary>
public class PositionBasedStrategy : ISymbolSearchStrategy
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<PositionBasedStrategy>? _logger;

    public string StrategyName => "PositionBased";

    public PositionBasedStrategy(RoslynWorkspace workspace, ILogger<PositionBasedStrategy>? logger = null)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public bool IsApplicableFor(SymbolKind kind)
    {
        // This strategy works for all symbol kinds if location is provided
        return true;
    }

    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request, CancellationToken ct = default)
    {
        // Require location information
        if (string.IsNullOrEmpty(request.FilePath) ||
            !request.Line.HasValue ||
            !request.Column.HasValue)
        {
            _logger?.LogDebug("PositionBasedStrategy skipped - no location information");
            return new List<ISymbol>();
        }

        try
        {
            var document = _workspace.GetDocument(request.FilePath);
            if (document == null)
            {
                _logger?.LogWarning("Document not found: {FilePath}", request.FilePath);
                return new List<ISymbol>();
            }

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            if (syntaxTree == null)
            {
                _logger?.LogWarning("No syntax tree for document: {FilePath}", request.FilePath);
                return new List<ISymbol>();
            }

            var text = await syntaxTree.GetTextAsync(ct);
            if (request.Line.Value < 1 || request.Line.Value > text.Lines.Count)
            {
                _logger?.LogWarning("Line {Line} out of range for document: {FilePath}",
                    request.Line.Value, request.FilePath);
                return new List<ISymbol>();
            }

            // Calculate position from line/column
            var line = text.Lines[request.Line.Value - 1];
            var column = Math.Max(0, Math.Min(request.Column.Value, line.Span.Length));
            var position = line.Start + column;

            // Use Roslyn's FindSymbolAtPositionAsync
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct);

            if (symbol != null)
            {
                _logger?.LogDebug("PositionBasedStrategy found symbol '{Name}' at {FilePath}:{Line}:{Column}",
                    symbol.Name, request.FilePath, request.Line.Value, column);
                return new List<ISymbol> { symbol };
            }

            _logger?.LogDebug("PositionBasedStrategy found no symbol at {FilePath}:{Line}:{Column}",
                request.FilePath, request.Line.Value, column);
            return new List<ISymbol>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in PositionBasedStrategy at {FilePath}:{Line}:{Column}",
                request.FilePath, request.Line, request.Column);
            return new List<ISymbol>();
        }
    }
}
