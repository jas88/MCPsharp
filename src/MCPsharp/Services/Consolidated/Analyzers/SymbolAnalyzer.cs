using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes symbol-level code elements including definitions, references, callers, and usage patterns.
/// </summary>
public class SymbolAnalyzer
{
    private readonly SymbolQueryService? _symbolQuery;
    private readonly ReferenceFinderService? _referenceFinder;
    private readonly RoslynWorkspace? _workspace;
    private readonly ILogger<SymbolAnalyzer> _logger;

    public SymbolAnalyzer(
        SymbolQueryService? symbolQuery = null,
        ReferenceFinderService? referenceFinder = null,
        RoslynWorkspace? workspace = null,
        ILogger<SymbolAnalyzer>? logger = null)
    {
        _symbolQuery = symbolQuery;
        _referenceFinder = referenceFinder;
        _workspace = workspace;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SymbolAnalyzer>.Instance;
    }

    public async Task<SymbolDefinition?> GetSymbolDefinitionAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var symbols = await _symbolQuery.FindSymbolsAsync(symbolName, context);
            var definition = symbols.FirstOrDefault();

            if (definition != null)
            {
                return new SymbolDefinition
                {
                    Name = definition.Name,
                    Kind = definition.Kind,
                    Location = new Location
                    {
                        FilePath = definition.File,
                        Line = definition.Line,
                        Column = definition.Column
                    },
                    ContainingType = definition.ContainerName,
                    Namespace = definition.ContainerName,
                    Accessibility = "Public",
                    IsStatic = false,
                    IsVirtual = false,
                    IsAbstract = false
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol definition for {Symbol}", symbolName);
        }

        return null;
    }

    public async Task<List<SymbolReference>?> GetSymbolReferencesAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_referenceFinder == null || _workspace == null) return null;

            var references = await _referenceFinder.FindReferencesAsync(symbolName, context);

            return (references?.References ?? []).Select(r => new SymbolReference
            {
                FilePath = r.File,
                Line = r.Line,
                Column = r.Column,
                ReferenceType = "Reference",
                ContainingMember = r.Context
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol references for {Symbol}", symbolName);
        }

        return null;
    }

    public async Task<List<SymbolCaller>?> GetSymbolCallersAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context, ct);
            if (references == null) return null;

            var callers = new List<SymbolCaller>();

            foreach (var reference in references)
            {
                try
                {
                    var callingSymbol = await _symbolQuery.GetSymbolAtLocationAsync(
                        reference.FilePath, reference.Line, reference.Column);

                    if (callingSymbol != null &&
                        (callingSymbol.Kind == "method" || callingSymbol.Kind == "property" || callingSymbol.Kind == "constructor"))
                    {
                        callers.Add(new SymbolCaller
                        {
                            FilePath = reference.FilePath,
                            Line = reference.Line,
                            Column = reference.Column,
                            CallerName = callingSymbol.Name,
                            CallType = "Direct",
                            IsAsync = callingSymbol.Name.Contains("Async")
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing caller for reference at {File}:{Line}",
                        reference.FilePath, reference.Line);
                }
            }

            return callers.DistinctBy(c => $"{c.CallerName}@{c.FilePath}:{c.Line}").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol callers for {Symbol}", symbolName);
        }

        return null;
    }

    public async Task<SymbolUsagePatterns?> AnalyzeSymbolUsageAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context, ct);
            var callers = await GetSymbolCallersAsync(symbolName, context, ct);

            if (references == null || callers == null) return null;

            var contexts = references
                .Where(r => !string.IsNullOrEmpty(r.ContainingMember))
                .GroupBy(r => r.ContainingMember!)
                .Select(g => new { Context = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .Select(x => x.Context)
                .ToList();

            var patterns = new List<string>();
            if (callers.Count > 10) patterns.Add("Frequently called");
            if (references.Count > callers.Count) patterns.Add("Multiple references per caller");
            if (contexts.Any(c => c.Contains("Test") || c.Contains("test"))) patterns.Add("Used in tests");

            return new SymbolUsagePatterns
            {
                TotalUsages = references.Count,
                UniqueCallers = callers.Count,
                CommonContexts = contexts,
                UsagePatterns = patterns
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing symbol usage patterns for {Symbol}", symbolName);
        }

        return null;
    }

    public async Task<List<RelatedSymbol>?> FindRelatedSymbolsAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var relatedSymbols = new List<RelatedSymbol>();

            var definition = await GetSymbolDefinitionAsync(symbolName, context, ct);
            if (definition == null) return null;

            if (!string.IsNullOrEmpty(definition.ContainingType))
            {
                var allSymbols = await _symbolQuery.GetAllTypesAsync();
                var sameTypeSymbols = allSymbols
                    .Where(s => s.ContainerName == definition.ContainingType)
                    .Take(20);
                relatedSymbols.AddRange(sameTypeSymbols
                    .Where(s => s.Name != symbolName)
                    .Select(s => new RelatedSymbol
                    {
                        Name = s.Name,
                        Relationship = "Same type",
                        Location = new Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.8
                    }));
            }

            if (!string.IsNullOrEmpty(definition.Namespace))
            {
                var sameNamespaceSymbols = await _symbolQuery.GetSymbolsInNamespaceAsync(definition.Namespace);
                relatedSymbols.AddRange(sameNamespaceSymbols
                    .Where(s => s.Name != symbolName && s.ContainerName != definition.ContainingType)
                    .Take(10)
                    .Select(s => new RelatedSymbol
                    {
                        Name = s.Name,
                        Relationship = "Same namespace",
                        Location = new Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.6
                    }));
            }

            var allTypesForSimilarity = await _symbolQuery.GetAllTypesAsync();
            var similarSymbols = allTypesForSimilarity
                .Where(s => Math.Abs(s.Name.Length - symbolName.Length) <= 2 &&
                           (s.Name.Contains(symbolName.Substring(0, Math.Min(3, symbolName.Length))) ||
                            symbolName.Contains(s.Name.Substring(0, Math.Min(3, s.Name.Length)))))
                .Where(s => s.Name != symbolName)
                .Take(5)
                .Select(s => new RelatedSymbol
                {
                    Name = s.Name,
                    Relationship = "Similar name",
                    Location = new Location
                    {
                        FilePath = s.File,
                        Line = s.Line,
                        Column = s.Column
                    },
                    Relevance = 0.4
                });

            relatedSymbols.AddRange(similarSymbols);

            return relatedSymbols
                .GroupBy(s => s.Name)
                .Select(g => g.First())
                .OrderByDescending(s => s.Relevance)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding related symbols for {Symbol}", symbolName);
        }

        return null;
    }

    public async Task<SymbolMetrics?> CalculateSymbolMetricsAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var definition = await GetSymbolDefinitionAsync(symbolName, context, ct);
            if (definition == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context, ct);

            var usageCount = references?.Count ?? 0;
            var parameterCount = 0;
            var linesOfCode = 0;

            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(usageCount + 1) - 0.23 * 10 - 16.2 * Math.Log(linesOfCode + 1));

            return new SymbolMetrics
            {
                UsageCount = usageCount,
                ParameterCount = parameterCount,
                LinesOfCode = linesOfCode,
                CyclomaticComplexity = 1,
                CognitiveComplexity = 1,
                MaintainabilityIndex = maintainabilityIndex
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating symbol metrics for {Symbol}", symbolName);
        }

        return null;
    }
}
