using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Selects the best symbol from a list of candidates based on
/// search criteria and scoring heuristics.
/// </summary>
public class SymbolSelector
{
    private readonly ILogger<SymbolSelector>? _logger;

    public SymbolSelector(ILogger<SymbolSelector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Select the best symbol from candidates
    /// </summary>
    public ISymbol? SelectBest(List<ISymbol> candidates, SymbolSearchRequest request)
    {
        if (candidates == null || !candidates.Any())
        {
            return null;
        }

        try
        {
            // Step 1: Normalize symbols (handle property accessors, constructors)
            var normalized = candidates
                .Select(NormalizeSymbol)
                .Where(s => s != null)
                .Cast<ISymbol>()
                .ToList();

            // Step 2: Filter by symbol kind if specified
            if (request.SymbolKind != SymbolKind.Any)
            {
                var filtered = normalized
                    .Where(s => MatchesSymbolKind(s, request.SymbolKind))
                    .ToList();

                if (filtered.Any())
                {
                    normalized = filtered;
                }
            }

            // Step 3: Filter by containing type if specified
            if (!string.IsNullOrEmpty(request.ContainingType))
            {
                var filtered = normalized
                    .Where(s => s.ContainingType?.Name == request.ContainingType)
                    .ToList();

                if (filtered.Any())
                {
                    normalized = filtered;
                }
            }

            // Step 4: Filter to source symbols only (not metadata)
            var sourceSymbols = normalized
                .Where(s => s.Locations.Any(l => l.IsInSource))
                .ToList();

            if (sourceSymbols.Any())
            {
                normalized = sourceSymbols;
            }

            // Step 5: If we have location info, prefer symbols from that file
            if (!string.IsNullOrEmpty(request.FilePath))
            {
                var fileSymbols = normalized
                    .Where(s => s.Locations.Any(l => l.GetLineSpan().Path == request.FilePath))
                    .ToList();

                if (fileSymbols.Any())
                {
                    normalized = fileSymbols;
                }
            }

            // Step 6: Score remaining candidates
            var scored = normalized
                .Select(s => new { Symbol = s, Score = CalculateScore(s, request) })
                .OrderByDescending(x => x.Score)
                .ToList();

            var best = scored.FirstOrDefault();

            if (best != null)
            {
                _logger?.LogDebug("Selected symbol '{Name}' with score {Score} from {Count} candidates",
                    best.Symbol.Name, best.Score, candidates.Count);
            }

            return best?.Symbol;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error selecting best symbol from {Count} candidates", candidates.Count);
            return candidates.FirstOrDefault();
        }
    }

    /// <summary>
    /// Normalize symbol (convert accessors to properties, constructors to types)
    /// </summary>
    private ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol == null)
            return null;

        // For property accessors, return the property
        if (symbol is IMethodSymbol methodSymbol)
        {
            if (methodSymbol.MethodKind == MethodKind.PropertyGet ||
                methodSymbol.MethodKind == MethodKind.PropertySet)
            {
                return methodSymbol.AssociatedSymbol as IPropertySymbol;
            }

            // For constructors, return the containing type
            if (methodSymbol.MethodKind == MethodKind.Constructor)
            {
                return methodSymbol.ContainingType;
            }
        }

        return symbol;
    }

    /// <summary>
    /// Check if symbol matches the requested kind
    /// </summary>
    private bool MatchesSymbolKind(ISymbol symbol, SymbolKind requestedKind)
    {
        if (symbol == null)
            return false;

        return requestedKind switch
        {
            SymbolKind.Class => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class },

            SymbolKind.Interface => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },

            SymbolKind.Method => symbol is IMethodSymbol methodSymbol &&
                                methodSymbol.MethodKind != MethodKind.Constructor &&
                                methodSymbol.MethodKind != MethodKind.PropertyGet &&
                                methodSymbol.MethodKind != MethodKind.PropertySet,

            SymbolKind.Property => symbol is IPropertySymbol,

            SymbolKind.Field => symbol is IFieldSymbol,

            SymbolKind.Parameter => symbol is IParameterSymbol,

            SymbolKind.Namespace => symbol is INamespaceSymbol,

            SymbolKind.Local => symbol is ILocalSymbol,

            SymbolKind.TypeParameter => symbol is ITypeParameterSymbol,

            _ => true // Any
        };
    }

    /// <summary>
    /// Calculate score for a symbol based on how well it matches the request
    /// </summary>
    private int CalculateScore(ISymbol symbol, SymbolSearchRequest request)
    {
        if (symbol == null || request == null)
            return 0;

        var score = 0;

        // Exact name match (case-sensitive)
        if (string.Equals(symbol.Name, request.Name, StringComparison.Ordinal))
            score += 100;
        else if (string.Equals(symbol.Name, request.Name, StringComparison.OrdinalIgnoreCase))
            score += 50;

        // Source symbols preferred over metadata
        if (symbol.Locations.Any(l => l.IsInSource))
            score += 50;

        // Matches containing type
        if (!string.IsNullOrEmpty(request.ContainingType) &&
            string.Equals(symbol.ContainingType?.Name, request.ContainingType,
                StringComparison.OrdinalIgnoreCase))
            score += 75;

        // Not compiler-generated
        if (!symbol.IsImplicitlyDeclared)
            score += 25;

        // Matches file path
        if (!string.IsNullOrEmpty(request.FilePath) &&
            symbol.Locations.Any(l => l.GetLineSpan().Path == request.FilePath))
            score += 30;

        // Prefer non-obsolete symbols
        var hasObsolete = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "ObsoleteAttribute");
        if (!hasObsolete)
            score += 10;

        return score;
    }
}
