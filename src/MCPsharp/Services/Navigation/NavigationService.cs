using System.Diagnostics;
using MCPsharp.Models.Navigation;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Implementation of navigation service for code navigation features
/// </summary>
public partial class NavigationService : INavigationService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ISymbolResolutionService _symbolResolver;
    private readonly INavigationCache _cache;
    private readonly ILogger<NavigationService> _logger;

    public NavigationService(
        RoslynWorkspace workspace,
        ISymbolResolutionService symbolResolver,
        INavigationCache cache,
        ILogger<NavigationService>? logger = null)
    {
        _workspace = workspace;
        _symbolResolver = symbolResolver;
        _cache = cache;
        _logger = logger ?? NullLogger<NavigationService>.Instance;
    }

    /// <summary>
    /// Navigate to the definition of a symbol at the specified location
    /// </summary>
    public async Task<NavigationResult> GoToDefinitionAsync(
        string filePath,
        int line,
        int column,
        bool includeContent = false)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve symbol at position
            var resolved = await _symbolResolver.ResolveSymbolAtPositionAsync(
                filePath, line, column,
                new SymbolResolutionOptions { ResolveToOriginalDefinition = true });

            if (resolved == null)
            {
                return new NavigationResult
                {
                    Success = false,
                    Message = "No symbol found at the specified location",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Handle external symbols
            if (!resolved.Symbol.Locations.Any(l => l.IsInSource))
            {
                return CreateExternalSymbolResult(resolved.Symbol, stopwatch.ElapsedMilliseconds);
            }

            // Get primary location for the definition
            var location = resolved.DeclarationLocation;

            // Add code context if requested
            if (includeContent)
            {
                location = await AddCodeContextAsync(location);
            }

            // Get alternatives if ambiguous
            List<NavigationLocation>? alternatives = null;
            if (resolved.Confidence == SymbolConfidence.Ambiguous && resolved.AlternativeSymbols != null)
            {
                alternatives = GetAlternativeLocations(resolved.AlternativeSymbols);
            }

            return new NavigationResult
            {
                Success = true,
                Location = location,
                Confidence = resolved.Confidence,
                Alternatives = alternatives,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogGoToDefinitionError(_logger, ex, filePath, line, column);

            return new NavigationResult
            {
                Success = false,
                Message = $"Error navigating to definition: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Find all related symbols (definitions, overrides, implementations)
    /// </summary>
    public async Task<MultiNavigationResult> FindAllRelatedAsync(
        string filePath,
        int line,
        int column)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var locations = new HashSet<NavigationLocation>(new NavigationLocationComparer());

            // Get definition
            var definition = await GoToDefinitionAsync(filePath, line, column);
            if (definition.Success && definition.Location != null)
            {
                locations.Add(definition.Location);
            }

            // Get overrides
            var overrides = await FindOverridesAsync(filePath, line, column);
            if (overrides.Success)
            {
                foreach (var loc in overrides.Locations)
                {
                    locations.Add(loc);
                }
            }

            // Get base symbol
            var baseSymbol = await FindBaseSymbolAsync(filePath, line, column);
            if (baseSymbol.Success && baseSymbol.Location != null)
            {
                locations.Add(baseSymbol.Location);
            }

            // Get implementations
            var implementation = await GoToImplementationAsync(filePath, line, column);
            if (implementation.Success && implementation.Location != null)
            {
                locations.Add(implementation.Location);
                if (implementation.Alternatives != null)
                {
                    foreach (var alt in implementation.Alternatives)
                    {
                        locations.Add(alt);
                    }
                }
            }

            return new MultiNavigationResult
            {
                Success = true,
                Locations = locations.OrderBy(l => l.FilePath).ThenBy(l => l.Line).ToList(),
                TotalFound = locations.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogFindAllRelatedError(_logger, ex, filePath, line, column);

            return new MultiNavigationResult
            {
                Success = false,
                Message = $"Error finding related symbols: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Helper methods
}
