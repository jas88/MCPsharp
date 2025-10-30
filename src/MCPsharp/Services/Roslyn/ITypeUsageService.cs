using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for analyzing type usage across the codebase
/// </summary>
public interface ITypeUsageService
{
    /// <summary>
    /// Find all usages of a specific type
    /// </summary>
    Task<TypeUsageResult?> FindTypeUsagesAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all usages of a type at a specific location
    /// </summary>
    Task<TypeUsageResult?> FindTypeUsagesAtLocationAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all usages of a specific type symbol
    /// </summary>
    Task<TypeUsageResult?> FindTypeUsagesAsync(INamedTypeSymbol typeSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all usages of a type by its full name
    /// </summary>
    Task<TypeUsageResult?> FindTypeUsagesByFullNameAsync(string fullTypeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all instantiations of a specific type
    /// </summary>
    Task<List<TypeUsageInfo>> FindInstantiationsAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all inheritance relationships for a type (who inherits from it, who it inherits from)
    /// </summary>
    Task<InheritanceAnalysis> AnalyzeInheritanceAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all interface implementations for a type
    /// </summary>
    Task<List<TypeUsageInfo>> FindInterfaceImplementationsAsync(string interfaceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all generic usages of a type
    /// </summary>
    Task<List<TypeUsageInfo>> FindGenericUsagesAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze type dependencies for a specific type
    /// </summary>
    Task<TypeDependencyAnalysis> AnalyzeTypeDependenciesAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find types that are similar to a given type (same base class, similar interface patterns)
    /// </summary>
    Task<List<MethodSignature>> FindSimilarTypesAsync(string typeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze type usage patterns across the codebase
    /// </summary>
    Task<TypeUsagePatternAnalysis> AnalyzeUsagePatternsAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find potential type refactorings (unused types, redundant types, etc.)
    /// </summary>
    Task<TypeRefactoringOpportunities> FindRefactoringOpportunitiesAsync(string? namespaceFilter = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Analysis of inheritance relationships
/// </summary>
public class InheritanceAnalysis
{
    public required string TargetType { get; init; }
    public required List<TypeUsageInfo> BaseClasses { get; init; }
    public required List<TypeUsageInfo> DerivedClasses { get; init; }
    public required List<TypeUsageInfo> ImplementedInterfaces { get; init; }
    public required List<TypeUsageInfo> InterfaceImplementations { get; init; }
    public required int InheritanceDepth { get; init; }
    public required List<string> InheritanceChain { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsInterface { get; init; }
    public bool IsSealed { get; init; }
}

/// <summary>
/// Analysis of type dependencies
/// </summary>
public class TypeDependencyAnalysis
{
    public required string TargetType { get; init; }
    public required List<TypeDependency> Dependencies { get; init; }
    public required List<TypeDependency> Dependents { get; init; }
    public required Dictionary<string, int> DependencyFrequency { get; init; }
    public required List<string> CircularDependencies { get; init; }
    public int TotalDependencies { get; init; }
    public int TotalDependents { get; init; }
    public bool HasCircularDependencies { get; init; }
}

/// <summary>
/// Represents a dependency between types
/// </summary>
public class TypeDependency
{
    public required string FromType { get; init; }
    public required string ToType { get; init; }
    public required TypeUsageKind DependencyKind { get; init; }
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required int UsageCount { get; init; }
}

/// <summary>
/// Analysis of type usage patterns
/// </summary>
public class TypeUsagePatternAnalysis
{
    public required Dictionary<string, TypeUsageStats> TypeStatistics { get; init; }
    public required List<UsagePattern> CommonPatterns { get; init; }
    public required List<string> MostUsedTypes { get; init; }
    public required List<string> LeastUsedTypes { get; init; }
    public required Dictionary<TypeUsageKind, int> UsageKindDistribution { get; init; }
    public required int TotalTypesAnalyzed { get; init; }
    public required int TotalUsagesFound { get; init; }
}

/// <summary>
/// Statistics for a specific type's usage
/// </summary>
public class TypeUsageStats
{
    public required string TypeName { get; init; }
    public required int TotalUsages { get; init; }
    public required Dictionary<TypeUsageKind, int> UsagesByKind { get; init; }
    public required List<string> FilesUsedIn { get; init; }
    public required bool IsPubliclyUsed { get; init; }
    public required bool IsTestOnly { get; init; }
    public required TimeSpan FirstUsage { get; init; }
    public required TimeSpan LastUsage { get; init; }
}

/// <summary>
/// Represents a usage pattern
/// </summary>
public class UsagePattern
{
    public required string Pattern { get; init; }
    public required int Frequency { get; init; }
    public required List<string> ExampleTypes { get; init; }
    public required double PercentageOfTotal { get; init; }
}

/// <summary>
/// Opportunities for type refactoring
/// </summary>
public class TypeRefactoringOpportunities
{
    public required List<MethodSignature> UnusedTypes { get; init; }
    public required List<MethodSignature> SingleImplementationInterfaces { get; init; }
    public required List<MethodSignature> PotentialSealedTypes { get; init; }
    public required List<MethodSignature> LargeTypes { get; init; }
    public required List<MethodSignature> TypesWithCircularDependencies { get; init; }
    public required List<MethodSignature> DuplicatedTypes { get; init; }
    public required int TotalOpportunities { get; init; }
    public required Dictionary<string, int> OpportunityBreakdown { get; init; }
}