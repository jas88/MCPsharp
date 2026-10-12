namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Location information
/// </summary>
public class Location
{
    public required string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// Symbol definition
/// </summary>
public class SymbolDefinition
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required Location Location { get; set; }
    public string? ContainingType { get; set; }
    public string? Namespace { get; set; }
    public string? Accessibility { get; set; }
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
}

/// <summary>
/// Symbol reference
/// </summary>
public class SymbolReference
{
    public required string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public required string ReferenceType { get; set; }
    public string? ContainingMember { get; set; }
}

/// <summary>
/// Symbol caller
/// </summary>
public class SymbolCaller
{
    public required string FilePath { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public required string CallerName { get; set; }
    public required string CallType { get; set; }
    public bool IsAsync { get; set; }
}

/// <summary>
/// Symbol usage patterns
/// </summary>
public class SymbolUsagePatterns
{
    public int TotalUsages { get; set; }
    public int UniqueCallers { get; set; }
    public List<string> CommonContexts { get; set; } = new();
    public List<string> UsagePatterns { get; set; } = new();
}

/// <summary>
/// Related symbol
/// </summary>
public class RelatedSymbol
{
    public required string Name { get; set; }
    public required string Relationship { get; set; }
    public required Location Location { get; set; }
    public double Relevance { get; set; }
}

/// <summary>
/// Symbol metrics
/// </summary>
public class SymbolMetrics
{
    public int CyclomaticComplexity { get; set; }
    public int CognitiveComplexity { get; set; }
    public int LinesOfCode { get; set; }
    public int ParameterCount { get; set; }
    public int UsageCount { get; set; }
    public double MaintainabilityIndex { get; set; }
}
