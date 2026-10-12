using MCPsharp.Models.Consolidated;
using Microsoft.CodeAnalysis;
using ConsolidatedLocation = MCPsharp.Models.Consolidated.Location;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    private async Task<SymbolDefinition?> GetSymbolDefinitionAsync(string symbolName, string? context)
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
                    Location = new MCPsharp.Models.Consolidated.Location
                    {
                        FilePath = definition.File,
                        Line = definition.Line,
                        Column = definition.Column
                    },
                    ContainingType = definition.ContainerName,
                    Namespace = definition.ContainerName,
                    Accessibility = "Public", // Default since SymbolResult doesn't have this info
                    IsStatic = false, // Default since SymbolResult doesn't have this info
                    IsVirtual = false, // Default since SymbolResult doesn't have this info
                    IsAbstract = false // Default since SymbolResult doesn't have this info
                };
            }
        }
        catch (Exception ex)
        {
            LogErrorGettingSymbolDefinitionFor(_logger, ex, symbolName);
        }

        return null;
    }

    private async Task<List<SymbolReference>?> GetSymbolReferencesAsync(string symbolName, string? context)
    {
        try
        {
            if (_referenceFinder == null || _workspace == null) return null;

            var references = await _referenceFinder.FindReferencesAsync(symbolName, context);
            if (references == null || references.References == null)
                return new List<SymbolReference>();

            return references.References.Select(r => new SymbolReference
            {
                FilePath = r.File,
                Line = r.Line,
                Column = r.Column,
                ReferenceType = "Reference", // Default since ReferenceLocation doesn't have ReferenceType
                ContainingMember = r.Context
            }).ToList();
        }
        catch (Exception ex)
        {
            LogErrorGettingSymbolReferencesFor(_logger, ex, symbolName);
        }

        return null;
    }

    private async Task<List<SymbolCaller>?> GetSymbolCallersAsync(string symbolName, string? context)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            // Get all references to the symbol
            var references = await GetSymbolReferencesAsync(symbolName, context);
            if (references == null) return null;

            var callers = new List<SymbolCaller>();

            foreach (var reference in references)
            {
                try
                {
                    // Get the symbol at the reference location to find the calling method
                    var callingSymbol = await _symbolQuery.GetSymbolAtLocationAsync(
                        reference.FilePath, reference.Line, reference.Column);

                    if (callingSymbol != null &&
                        (callingSymbol.Kind == "method" || callingSymbol.Kind == "property" || callingSymbol.Kind == "constructor"))
                    {
                        callers.Add(new SymbolCaller
                        {
                            FilePath = reference.FilePath, // Use reference location since SymbolInfo doesn't have file location
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
                    LogErrorProcessingCallerForReference(_logger, ex, reference.FilePath, reference.Line);
                }
            }

            return callers.DistinctBy(c => $"{c.CallerName}@{c.FilePath}:{c.Line}").ToList();
        }
        catch (Exception ex)
        {
            LogErrorGettingSymbolCallersFor(_logger, ex, symbolName);
        }

        return null;
    }

    private async Task<SymbolUsagePatterns?> AnalyzeSymbolUsageAsync(string symbolName, string? context)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context);
            var callers = await GetSymbolCallersAsync(symbolName, context);

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
            LogErrorAnalyzingSymbolUsagePatterns(_logger, ex, symbolName);
        }

        return null;
    }

    private async Task<List<RelatedSymbol>?> FindRelatedSymbolsAsync(string symbolName, string? context)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var relatedSymbols = new List<RelatedSymbol>();

            // Get the symbol definition to understand its context
            var definition = await GetSymbolDefinitionAsync(symbolName, context);
            if (definition == null) return null;

            // Find symbols in the same containing type (simplified - would need type symbol info)
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
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.8
                    }));
            }

            // Find symbols in the same namespace
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
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.6
                    }));
            }

            // Find symbols with similar names
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
                    Location = new ConsolidatedLocation
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
            LogErrorFindingRelatedSymbolsFor(_logger, ex, symbolName);
        }

        return null;
    }

    private async Task<SymbolMetrics?> CalculateSymbolMetricsAsync(string symbolName, string? context)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var definition = await GetSymbolDefinitionAsync(symbolName, context);
            if (definition == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context);

            // Basic metrics
            var usageCount = references?.Count ?? 0;
            var parameterCount = 0; // Would need to parse method signature for this
            var linesOfCode = 0; // Would need to analyze method body for this

            // Calculate maintainability index (simplified version)
            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(usageCount + 1) - 0.23 * 10 - 16.2 * Math.Log(linesOfCode + 1));

            return new SymbolMetrics
            {
                UsageCount = usageCount,
                ParameterCount = parameterCount,
                LinesOfCode = linesOfCode,
                CyclomaticComplexity = 1, // Simplified
                CognitiveComplexity = 1, // Simplified
                MaintainabilityIndex = maintainabilityIndex
            };
        }
        catch (Exception ex)
        {
            LogErrorCalculatingSymbolMetricsFor(_logger, ex, symbolName);
        }

        return null;
    }
}
