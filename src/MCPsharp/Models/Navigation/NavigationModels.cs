using Microsoft.CodeAnalysis;

namespace MCPsharp.Models.Navigation;

/// <summary>
/// Result of a navigation request to a single location
/// </summary>
public class NavigationResult
{
    public bool Success { get; init; }
    public NavigationLocation? Location { get; init; }
    public SymbolConfidence Confidence { get; init; } = SymbolConfidence.Exact;
    public List<NavigationLocation>? Alternatives { get; init; }
    public bool IsExternal { get; init; }
    public ExternalSymbolInfo? ExternalInfo { get; init; }
    public string? Message { get; init; }
    public long ExecutionTimeMs { get; init; }
}

/// <summary>
/// Result of a navigation request that returns multiple locations
/// </summary>
public class MultiNavigationResult
{
    public bool Success { get; init; }
    public List<NavigationLocation> Locations { get; init; } = new();
    public NavigationSymbolInfo? BaseSymbol { get; init; }
    public int TotalFound { get; init; }
    public bool HasMore { get; init; }
    public string? Message { get; init; }
    public long ExecutionTimeMs { get; init; }
}

/// <summary>
/// Represents a navigation target location with associated symbol information
/// </summary>
public class NavigationLocation
{
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public NavigationSymbolInfo? Symbol { get; init; }
    public CodeContext? Context { get; init; }
    public bool IsPartial { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>
/// Information about a symbol in navigation results
/// </summary>
public class NavigationSymbolInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string? Signature { get; init; }
    public string? ContainingType { get; init; }
    public string? Namespace { get; init; }
    public string? ReturnType { get; init; }
    public List<string>? Parameters { get; init; }
    public string? Accessibility { get; init; }
    public List<string>? Modifiers { get; init; }
    public string? Documentation { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public bool IsSealed { get; init; }
    public bool IsStatic { get; init; }
    public bool IsExplicitInterfaceImplementation { get; init; }
}

/// <summary>
/// Code context around a navigation location
/// </summary>
public class CodeContext
{
    public List<string> Before { get; init; } = new();
    public required string Target { get; init; }
    public List<string> After { get; init; } = new();
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}

/// <summary>
/// Information about external symbols (in metadata/assemblies)
/// </summary>
public class ExternalSymbolInfo
{
    public string? AssemblyName { get; init; }
    public string? AssemblyPath { get; init; }
    public string? TypeName { get; init; }
    public string? MemberName { get; init; }
    public string? Documentation { get; init; }
    public bool CanDecompile { get; init; }
    public string? SourceLink { get; init; }
}

/// <summary>
/// Confidence level for symbol resolution
/// </summary>
public enum SymbolConfidence
{
    /// <summary>
    /// Exact match with no ambiguity
    /// </summary>
    Exact = 100,

    /// <summary>
    /// High confidence but some minor ambiguity possible
    /// </summary>
    High = 80,

    /// <summary>
    /// Medium confidence, multiple candidates but one is clearly better
    /// </summary>
    Medium = 60,

    /// <summary>
    /// Low confidence, best guess among multiple candidates
    /// </summary>
    Low = 40,

    /// <summary>
    /// Multiple equally valid candidates, user should choose
    /// </summary>
    Ambiguous = 20
}

/// <summary>
/// Options for symbol resolution
/// </summary>
public class SymbolResolutionOptions
{
    public bool ResolveToOriginalDefinition { get; init; } = true;
    public bool IncludeOverrides { get; init; } = false;
    public bool IncludeImplementations { get; init; } = false;
    public bool PreferImplementationOverInterface { get; init; } = true;
    public bool IncludeMetadata { get; init; } = false;
}

/// <summary>
/// A resolved symbol with additional context
/// </summary>
public class ResolvedSymbol
{
    public required ISymbol Symbol { get; init; }
    public required NavigationLocation DeclarationLocation { get; init; }
    public SymbolConfidence Confidence { get; init; } = SymbolConfidence.Exact;
    public List<ISymbol>? AlternativeSymbols { get; init; }
    public List<NavigationLocation>? AllLocations { get; init; }
}

/// <summary>
/// Information about a type's inheritance hierarchy
/// </summary>
public class HierarchyInfo
{
    public required INamedTypeSymbol Type { get; init; }
    public INamedTypeSymbol? BaseType { get; init; }
    public List<INamedTypeSymbol> Interfaces { get; init; } = new();
    public Lazy<Task<List<INamedTypeSymbol>>>? DerivedTypesLoader { get; init; }
    public List<INamedTypeSymbol>? CachedDerivedTypes { get; set; }
    public DateTime CacheTime { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Options for hierarchy analysis
/// </summary>
public class HierarchyOptions
{
    public bool IncludeBase { get; init; } = true;
    public bool IncludeInterfaces { get; init; } = true;
    public bool IncludeDerived { get; init; } = false;
    public int MaxDepth { get; init; } = -1;
    public bool IncludeSealed { get; init; } = true;
}

/// <summary>
/// Information about method overloads
/// </summary>
public class OverloadInfo
{
    public required string MethodName { get; init; }
    public required string ContainingType { get; init; }
    public List<MethodOverload> Overloads { get; init; } = new();
    public int TotalOverloads { get; init; }
}

/// <summary>
/// Information about a single method overload
/// </summary>
public class MethodOverload
{
    public required string Signature { get; init; }
    public List<string> Parameters { get; init; } = new();
    public required string ReturnType { get; init; }
    public required NavigationLocation Location { get; init; }
    public bool IsCurrentMethod { get; init; }
    public bool IsExtensionMethod { get; init; }
    public bool IsGeneric { get; init; }
    public List<string>? GenericConstraints { get; init; }
    public string? Documentation { get; init; }
}