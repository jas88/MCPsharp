using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Strategy interface for symbol search operations.
/// Each strategy implements a specific approach to finding symbols
/// (e.g., compilation-level search, scope-local search, position-based search).
/// </summary>
public interface ISymbolSearchStrategy
{
    /// <summary>
    /// Search for symbols matching the given request
    /// </summary>
    /// <param name="request">Search criteria</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching symbols (may be empty)</returns>
    Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if this strategy is applicable for the given symbol kind
    /// </summary>
    /// <param name="kind">Symbol kind to check</param>
    /// <returns>True if this strategy can handle this symbol kind</returns>
    bool IsApplicableFor(SymbolKind kind);

    /// <summary>
    /// Strategy name for logging and debugging
    /// </summary>
    string StrategyName { get; }
}

/// <summary>
/// Request object for symbol search operations
/// </summary>
public class SymbolSearchRequest
{
    /// <summary>
    /// Symbol name to search for (required)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Symbol kind filter (Any means no filter)
    /// </summary>
    public SymbolKind SymbolKind { get; init; } = SymbolKind.Any;

    /// <summary>
    /// Optional containing type name for disambiguation
    /// </summary>
    public string? ContainingType { get; init; }

    /// <summary>
    /// Optional file path for location-based search
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Optional line number (1-based) for location-based search
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Optional column number (0-based) for location-based search
    /// </summary>
    public int? Column { get; init; }
}

/// <summary>
/// Result of symbol search operation
/// </summary>
public class SymbolSearchResult
{
    /// <summary>
    /// The selected best symbol (null if not found)
    /// </summary>
    public ISymbol? Symbol { get; init; }

    /// <summary>
    /// All candidate symbols found (includes selected symbol)
    /// </summary>
    public List<ISymbol> Candidates { get; init; } = new();

    /// <summary>
    /// Strategy that found the symbol
    /// </summary>
    public string? UsedStrategy { get; init; }

    /// <summary>
    /// Whether multiple equally-good candidates were found
    /// </summary>
    public bool IsAmbiguous { get; init; }

    /// <summary>
    /// Create a successful result with a single symbol
    /// </summary>
    public static SymbolSearchResult Success(ISymbol symbol, string strategy)
    {
        return new SymbolSearchResult
        {
            Symbol = symbol,
            Candidates = new List<ISymbol> { symbol },
            UsedStrategy = strategy,
            IsAmbiguous = false
        };
    }

    /// <summary>
    /// Create a result with multiple candidates
    /// </summary>
    public static SymbolSearchResult Multiple(List<ISymbol> candidates, ISymbol? selected, string strategy)
    {
        return new SymbolSearchResult
        {
            Symbol = selected,
            Candidates = candidates,
            UsedStrategy = strategy,
            IsAmbiguous = candidates.Count > 1
        };
    }

    /// <summary>
    /// Create a not-found result
    /// </summary>
    public static SymbolSearchResult NotFound()
    {
        return new SymbolSearchResult
        {
            Symbol = null,
            Candidates = new List<ISymbol>(),
            UsedStrategy = null,
            IsAmbiguous = false
        };
    }
}
