using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Unified service for finding symbols across the workspace.
/// Orchestrates multiple search strategies to handle different symbol types
/// (compilation-level symbols, scope-local symbols, position-based lookup).
/// </summary>
public partial class SymbolFinderService
{
    private readonly List<ISymbolSearchStrategy> _strategies;
    private readonly SymbolSelector _selector;
    private readonly ILogger<SymbolFinderService> _logger;

    public SymbolFinderService(
        RoslynWorkspace workspace,
        ILogger<SymbolFinderService>? logger = null,
        ILogger<CompilationStrategy>? compilationLogger = null,
        ILogger<ScopeLocalStrategy>? scopeLocalLogger = null,
        ILogger<PositionBasedStrategy>? positionLogger = null,
        ILogger<SymbolSelector>? selectorLogger = null)
    {
        _logger = logger ?? NullLogger<SymbolFinderService>.Instance;

        // Initialize selector
        _selector = new SymbolSelector(selectorLogger);

        // Initialize strategies in order of preference
        _strategies = new List<ISymbolSearchStrategy>
        {
            // Position-based is most precise when location is available
            new PositionBasedStrategy(workspace, positionLogger),

            // Compilation strategy for types, members, namespaces
            new CompilationStrategy(workspace, compilationLogger),

            // Scope-local strategy for locals and parameters
            new ScopeLocalStrategy(workspace, scopeLocalLogger)
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
            LogInvalidRequest(_logger);
            return SymbolSearchResult.NotFound();
        }

        LogSearching(_logger, request.Name, request.SymbolKind);

        var allCandidates = new List<ISymbol>();
        string? usedStrategy = null;

        // Try each applicable strategy
        foreach (var strategy in _strategies)
        {
            // Skip if strategy doesn't apply to this symbol kind
            if (!strategy.IsApplicableFor(request.SymbolKind))
            {
                LogSkippingStrategy(_logger, strategy.StrategyName, request.SymbolKind);
                continue;
            }

            try
            {
                LogTryingStrategy(_logger, strategy.StrategyName);
                var candidates = await strategy.SearchAsync(request, ct);

                if (candidates.Count > 0)
                {
                    allCandidates.AddRange(candidates);
                    usedStrategy ??= strategy.StrategyName;

                    LogStrategyFound(_logger, strategy.StrategyName, candidates.Count);

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
                LogStrategyError(_logger, ex, strategy.StrategyName);
            }
        }

        // Remove duplicates
        allCandidates = allCandidates
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();

        if (allCandidates.Count == 0)
        {
            LogNoSymbolsFound(_logger, request.Name);
            return SymbolSearchResult.NotFound();
        }

        // Select the best candidate
        var bestSymbol = _selector.SelectBest(allCandidates, request);

        if (bestSymbol == null)
        {
            LogCouldNotSelectBest(_logger, allCandidates.Count, request.Name);
            return SymbolSearchResult.NotFound();
        }

        LogFoundSymbol(_logger, bestSymbol.Name, usedStrategy, allCandidates.Count);

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
                LogStrategyError(_logger, ex, strategy.StrategyName);
            }
        }

        // Remove duplicates
        return allCandidates
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();
    }

    /// <summary>
    /// Register a custom search strategy.
    /// Useful for extending with domain-specific search logic.
    /// </summary>
    public void RegisterStrategy(ISymbolSearchStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        _strategies.Add(strategy);
        LogRegisteredStrategy(_logger, strategy.StrategyName);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid symbol search request")]
    private static partial void LogInvalidRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching for symbol '{Name}' (kind: {Kind})")]
    private static partial void LogSearching(ILogger logger, string name, SymbolKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping {Strategy} - not applicable for {Kind}")]
    private static partial void LogSkippingStrategy(ILogger logger, string strategy, SymbolKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Trying {Strategy} strategy")]
    private static partial void LogTryingStrategy(ILogger logger, string strategy);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Strategy} found {Count} candidates")]
    private static partial void LogStrategyFound(ILogger logger, string strategy, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in {Strategy} strategy")]
    private static partial void LogStrategyError(ILogger logger, Exception exception, string strategy);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No symbols found for '{Name}'")]
    private static partial void LogNoSymbolsFound(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Found {Count} candidates but could not select best for '{Name}'")]
    private static partial void LogCouldNotSelectBest(ILogger logger, int count, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found symbol '{Name}' using {Strategy} ({Count} total candidates)")]
    private static partial void LogFoundSymbol(ILogger logger, string name, string? strategy, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered custom strategy: {Strategy}")]
    private static partial void LogRegisteredStrategy(ILogger logger, string strategy);
}
