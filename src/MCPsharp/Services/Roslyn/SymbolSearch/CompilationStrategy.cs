using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn.SymbolSearch;

/// <summary>
/// Search strategy for compilation-level symbols (types, members, namespaces).
/// Uses Roslyn's GetSymbolsWithName() API which is efficient for symbols
/// in the compilation's symbol table.
/// </summary>
public class CompilationStrategy : ISymbolSearchStrategy
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<CompilationStrategy>? _logger;

    public string StrategyName => "Compilation";

    public CompilationStrategy(RoslynWorkspace workspace, ILogger<CompilationStrategy>? logger = null)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public bool IsApplicableFor(SymbolKind kind)
    {
        // This strategy works for all compilation-level symbols
        return kind switch
        {
            SymbolKind.Class => true,
            SymbolKind.Interface => true,
            SymbolKind.Method => true,
            SymbolKind.Property => true,
            SymbolKind.Field => true,
            SymbolKind.Namespace => true,
            SymbolKind.TypeParameter => true,
            SymbolKind.Any => true,
            // Does NOT work for locals/parameters (scope-local symbols)
            SymbolKind.Local => false,
            SymbolKind.Parameter => false,
            _ => false
        };
    }

    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request, CancellationToken ct = default)
    {
        // CRITICAL: Use the current solution's compilation, not the cached compilation
        // Symbols from a cached compilation won't work correctly with Renamer API
        var project = _workspace.Solution.Projects.FirstOrDefault();
        if (project == null)
        {
            _logger?.LogWarning("No project available for symbol search");
            return new List<ISymbol>();
        }

        var compilation = await project.GetCompilationAsync(ct);
        if (compilation == null)
        {
            _logger?.LogWarning("No compilation available for symbol search");
            return new List<ISymbol>();
        }

        var results = new List<ISymbol>();

        // Try multiple SymbolFilter values to maximize coverage
        var filters = new[]
        {
            SymbolFilter.TypeAndMember,
            SymbolFilter.Type,
            SymbolFilter.Member,
            SymbolFilter.Namespace
        };

        foreach (var filter in filters)
        {
            try
            {
                var symbols = compilation.GetSymbolsWithName(
                    n => string.Equals(n, request.Name, StringComparison.OrdinalIgnoreCase),
                    filter,
                    ct);

                results.AddRange(symbols.Where(s => s != null));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error searching symbols with filter {Filter}", filter);
            }
        }

        // TypeParameters are not returned by GetSymbolsWithName, we need to search through types
        // to find their type parameters
        if (request.SymbolKind == SymbolKind.TypeParameter || request.SymbolKind == SymbolKind.Any)
        {
            try
            {
                var allTypes = compilation.GetSymbolsWithName(
                    _ => true,
                    SymbolFilter.Type,
                    ct);

                foreach (var typeSymbol in allTypes.OfType<INamedTypeSymbol>())
                {
                    // Check type parameters on the type itself
                    foreach (var typeParam in typeSymbol.TypeParameters)
                    {
                        if (string.Equals(typeParam.Name, request.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(typeParam);
                        }
                    }

                    // Check type parameters on methods within the type
                    foreach (var member in typeSymbol.GetMembers())
                    {
                        if (member is IMethodSymbol methodSymbol)
                        {
                            foreach (var methodTypeParam in methodSymbol.TypeParameters)
                            {
                                if (string.Equals(methodTypeParam.Name, request.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    results.Add(methodTypeParam);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error searching type parameters");
            }
        }

        // Remove duplicates
        results = results
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<ISymbol>()
            .ToList();

        _logger?.LogDebug("CompilationStrategy found {Count} candidates for '{Name}'",
            results.Count, request.Name);

        return results;
    }
}
