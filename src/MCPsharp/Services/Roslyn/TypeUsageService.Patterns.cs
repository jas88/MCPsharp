using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Usage pattern analysis and refactoring opportunity detection.
/// </summary>
public partial class TypeUsageService
{
    public Task<List<MethodSignature>> FindSimilarTypesAsync(string typeName, CancellationToken cancellationToken = default)
    {
        // This is a placeholder implementation
        // In practice, you'd want to analyze base classes, interfaces, and patterns
        return Task.FromResult(new List<MethodSignature>());
    }

    public async Task<TypeUsagePatternAnalysis> AnalyzeUsagePatternsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        if (!_workspace.IsInitialized)
            return new TypeUsagePatternAnalysis
            {
                TypeStatistics = new Dictionary<string, TypeUsageStats>(),
                CommonPatterns = new List<UsagePattern>(),
                MostUsedTypes = new List<string>(),
                LeastUsedTypes = new List<string>(),
                UsageKindDistribution = new Dictionary<TypeUsageKind, int>(),
                TotalTypesAnalyzed = 0,
                TotalUsagesFound = 0
            };

        var typeStatistics = new Dictionary<string, TypeUsageStats>();
        var usageKindDistribution = new Dictionary<TypeUsageKind, int>();
        var allTypes = new List<string>();

        // Analyze all types in the namespace
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (symbol == null)
                    continue;

                // Check namespace filter
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var ns = symbol.ContainingNamespace?.ToDisplayString();
                    if (ns == null || !ns.Contains(namespaceFilter))
                        continue;
                }

                var typeResult = await FindTypeUsagesAsync(symbol, cancellationToken);
                if (typeResult != null)
                {
                    allTypes.Add(symbol.Name);
                    typeStatistics[symbol.Name] = new TypeUsageStats
                    {
                        TypeName = symbol.Name,
                        TotalUsages = typeResult.TotalUsages,
                        UsagesByKind = typeResult.UsagesByKind.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.Count),
                        FilesUsedIn = typeResult.UsedInFiles.ToList(),
                        IsPubliclyUsed = typeResult.Usages.Any(u => u.AccessModifier == "public"),
                        IsTestOnly = typeResult.UsedInFiles.All(f => IsTestFile(f)),
                        FirstUsage = TimeSpan.Zero, // Placeholder
                        LastUsage = TimeSpan.Zero   // Placeholder
                    };

                    // Update usage kind distribution
                    foreach (var kvp in typeResult.UsagesByKind)
                    {
                        usageKindDistribution[kvp.Key] = usageKindDistribution.GetValueOrDefault(kvp.Key, 0) + kvp.Value.Count;
                    }
                }
            }
        }

        // Find common patterns
        var commonPatterns = new List<UsagePattern>();
        // This would require more sophisticated analysis in practice

        var mostUsedTypes = typeStatistics
            .OrderByDescending(kvp => kvp.Value.TotalUsages)
            .Take(10)
            .Select(kvp => kvp.Key)
            .ToList();

        var leastUsedTypes = typeStatistics
            .OrderBy(kvp => kvp.Value.TotalUsages)
            .Take(10)
            .Select(kvp => kvp.Key)
            .ToList();

        return new TypeUsagePatternAnalysis
        {
            TypeStatistics = typeStatistics,
            CommonPatterns = commonPatterns,
            MostUsedTypes = mostUsedTypes,
            LeastUsedTypes = leastUsedTypes,
            UsageKindDistribution = usageKindDistribution,
            TotalTypesAnalyzed = typeStatistics.Count,
            TotalUsagesFound = typeStatistics.Values.Sum(s => s.TotalUsages)
        };
    }

    public async Task<TypeRefactoringOpportunities> FindRefactoringOpportunitiesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default)
    {
        var patternAnalysis = await AnalyzeUsagePatternsAsync(namespaceFilter, cancellationToken);
        var unusedTypes = new List<MethodSignature>();
        var singleImplementationInterfaces = new List<MethodSignature>();
        var potentialSealedTypes = new List<MethodSignature>();
        var largeTypes = new List<MethodSignature>();
        var typesWithCircularDependencies = new List<MethodSignature>();
        var duplicatedTypes = new List<MethodSignature>();

        // Find unused types
        foreach (var stats in patternAnalysis.TypeStatistics.Values)
        {
            if (stats.TotalUsages == 0)
            {
                unusedTypes.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }

            // Find single implementation interfaces
            if (stats.TypeName.StartsWith('I') && stats.TotalUsages == 1)
            {
                singleImplementationInterfaces.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }

            // Find potential sealed types (classes that are never inherited)
            if (!stats.TypeName.StartsWith('I') &&
            stats.UsagesByKind.GetValueOrDefault(TypeUsageKind.BaseClass, 0) == 0)
            {
                potentialSealedTypes.Add(new MethodSignature
                {
                    Name = stats.TypeName,
                    ContainingType = stats.TypeName,
                    ReturnType = "void",
                    Accessibility = "public"
                });
            }
        }

        var totalOpportunities = unusedTypes.Count + singleImplementationInterfaces.Count +
                               potentialSealedTypes.Count + largeTypes.Count +
                               typesWithCircularDependencies.Count + duplicatedTypes.Count;

        var opportunityBreakdown = new Dictionary<string, int>
        {
            ["Unused Types"] = unusedTypes.Count,
            ["Single Implementation Interfaces"] = singleImplementationInterfaces.Count,
            ["Potential Sealed Types"] = potentialSealedTypes.Count,
            ["Large Types"] = largeTypes.Count,
            ["Types with Circular Dependencies"] = typesWithCircularDependencies.Count,
            ["Duplicated Types"] = duplicatedTypes.Count
        };

        return new TypeRefactoringOpportunities
        {
            UnusedTypes = unusedTypes,
            SingleImplementationInterfaces = singleImplementationInterfaces,
            PotentialSealedTypes = potentialSealedTypes,
            LargeTypes = largeTypes,
            TypesWithCircularDependencies = typesWithCircularDependencies,
            DuplicatedTypes = duplicatedTypes,
            TotalOpportunities = totalOpportunities,
            OpportunityBreakdown = opportunityBreakdown
        };
    }
}
