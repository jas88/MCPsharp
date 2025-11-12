using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Unified service for finding symbols across the workspace.
/// Orchestrates multiple search strategies to handle different symbol types
/// (compilation-level symbols, scope-local symbols, position-based lookup).
/// </summary>
public class SymbolFinderService
{
    private readonly RoslynWorkspace _workspace;
    private readonly List<ISymbolSearchStrategy> _strategies;
    private readonly SymbolSelector _selector;
    private readonly ILogger<SymbolFinderService>? _logger;

    public SymbolFinderService(
        RoslynWorkspace workspace,
        ILogger<SymbolFinderService>? logger = null,
        ILogger<CompilationStrategy>? compilationLogger = null,
        ILogger<ScopeLocalStrategy>? scopeLocalLogger = null,
        ILogger<PositionBasedStrategy>? positionLogger = null,
        ILogger<SymbolSelector>? selectorLogger = null)
    {
        _workspace = workspace;
        _logger = logger;

        // Initialize selector
        _selector = new SymbolSelector(selectorLogger);

        // Initialize strategies in order of preference
        _strategies = new List<ISymbolSearchStrategy>
        {
            // Position-based is most precise when location is available
            new PositionBasedStrategy(_workspace, positionLogger),

            // Compilation strategy for types, members, namespaces
            new CompilationStrategy(_workspace, compilationLogger),

            // Scope-local strategy for locals and parameters
            new ScopeLocalStrategy(_workspace, scopeLocalLogger)
        };
    }

    /// <summary>
    /// Find a symbol matching the search request.
    /// Tries multiple strategies in order until a symbol is found.
    /// </summary>
    public async Task<SymbolSearchResult> FindSymbolAsync(
        SymbolSearchRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            _logger?.LogWarning("Invalid symbol search request");
            return SymbolSearchResult.NotFound();
        }

        _logger?.LogDebug("Searching for symbol '{Name}' (kind: {Kind})",
            request.Name, request.SymbolKind);

        var allCandidates = new List<ISymbol>();
        string? usedStrategy = null;

        // Try each applicable strategy
        foreach (var strategy in _strategies)
        {
            // Skip if strategy doesn't apply to this symbol kind
            if (!strategy.IsApplicableFor(request.SymbolKind))
            {
                _logger?.LogDebug("Skipping {Strategy} - not applicable for {Kind}",
                    strategy.StrategyName, request.SymbolKind);
                continue;
            }

            try
            {
                _logger?.LogDebug("Trying {Strategy} strategy", strategy.StrategyName);
                var candidates = await strategy.SearchAsync(request, ct);

                if (candidates.Any())
                {
                    allCandidates.AddRange(candidates);
                    usedStrategy ??= strategy.StrategyName;

                    _logger?.LogDebug("{Strategy} found {Count} candidates",
                        strategy.StrategyName, candidates.Count);

                    // If position-based strategy found something, use it immediately
                    // (most precise)
                    if (strategy is PositionBasedStrategy)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in {Strategy} strategy", strategy.StrategyName);
            }
        }

        // Remove duplicates
        allCandidates = allCandidates
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<ISymbol>()
            .ToList();

        if (!allCandidates.Any())
        {
            _logger?.LogDebug("No symbols found for '{Name}'", request.Name);
            return SymbolSearchResult.NotFound();
        }

        // Select the best candidate
        var bestSymbol = _selector.SelectBest(allCandidates, request);

        if (bestSymbol == null)
        {
            _logger?.LogWarning("Found {Count} candidates but could not select best for '{Name}'",
                allCandidates.Count, request.Name);
            return SymbolSearchResult.NotFound();
        }

        _logger?.LogInformation("Found symbol '{Name}' using {Strategy} ({Count} total candidates)",
            bestSymbol.Name, usedStrategy, allCandidates.Count);

        return SymbolSearchResult.Multiple(allCandidates, bestSymbol, usedStrategy ?? "Unknown");
    }

    /// <summary>
    /// Find all symbols matching the search request (no selection).
    /// Useful for finding all overloads or partial class parts.
    /// </summary>
    public async Task<List<ISymbol>> FindAllSymbolsAsync(
        SymbolSearchRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return new List<ISymbol>();
        }

        var allCandidates = new List<ISymbol>();

        foreach (var strategy in _strategies)
        {
            if (!strategy.IsApplicableFor(request.SymbolKind))
            {
                continue;
            }

            try
            {
                var candidates = await strategy.SearchAsync(request, ct);
                allCandidates.AddRange(candidates);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in {Strategy} strategy", strategy.StrategyName);
            }
        }

        // Remove duplicates
        return allCandidates
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<ISymbol>()
            .ToList();
    }

    /// <summary>
    /// Register a custom search strategy.
    /// Useful for extending with domain-specific search logic.
    /// </summary>
    public void RegisterStrategy(ISymbolSearchStrategy strategy)
    {
        if (strategy == null)
            throw new ArgumentNullException(nameof(strategy));

        _strategies.Add(strategy);
        _logger?.LogInformation("Registered custom strategy: {Strategy}", strategy.StrategyName);
    }
}
