using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Search strategy that finds symbols at a specific position in a file.
/// This is the most precise strategy when location information is available.
/// Uses Roslyn's SymbolFinder.FindSymbolAtPositionAsync().
/// </summary>
public partial class PositionBasedStrategy : ISymbolSearchStrategy
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<PositionBasedStrategy> _logger;

    public string StrategyName => "PositionBased";

    public PositionBasedStrategy(RoslynWorkspace workspace, ILogger<PositionBasedStrategy>? logger = null)
    {
        _workspace = workspace;
        _logger = logger ?? NullLogger<PositionBasedStrategy>.Instance;
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
            LogSkippedNoLocation(_logger);
            return new List<ISymbol>();
        }

        try
        {
            var document = _workspace.GetDocument(request.FilePath);
            if (document == null)
            {
                LogDocumentNotFound(_logger, request.FilePath);
                return new List<ISymbol>();
            }

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            if (syntaxTree == null)
            {
                LogNoSyntaxTree(_logger, request.FilePath);
                return new List<ISymbol>();
            }

            var text = await syntaxTree.GetTextAsync(ct);
            if (request.Line.Value < 1 || request.Line.Value > text.Lines.Count)
            {
                LogLineOutOfRange(_logger, request.Line.Value, request.FilePath);
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
                LogFoundSymbol(_logger, symbol.Name, request.FilePath, request.Line.Value, column);
                return new List<ISymbol> { symbol };
            }

            LogNoSymbolFound(_logger, request.FilePath, request.Line.Value, column);
            return new List<ISymbol>();
        }
        catch (Exception ex)
        {
            LogSearchError(_logger, ex, request.FilePath, request.Line, request.Column);
            return new List<ISymbol>();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "PositionBasedStrategy skipped - no location information")]
    private static partial void LogSkippedNoLocation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document not found: {FilePath}")]
    private static partial void LogDocumentNotFound(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No syntax tree for document: {FilePath}")]
    private static partial void LogNoSyntaxTree(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Line {Line} out of range for document: {FilePath}")]
    private static partial void LogLineOutOfRange(ILogger logger, int line, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PositionBasedStrategy found symbol '{Name}' at {FilePath}:{Line}:{Column}")]
    private static partial void LogFoundSymbol(ILogger logger, string name, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PositionBasedStrategy found no symbol at {FilePath}:{Line}:{Column}")]
    private static partial void LogNoSymbolFound(ILogger logger, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in PositionBasedStrategy at {FilePath}:{Line}:{Column}")]
    private static partial void LogSearchError(ILogger logger, Exception exception, string? filePath, int? line, int? column);
}
