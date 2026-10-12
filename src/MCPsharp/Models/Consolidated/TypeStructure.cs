namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Type member
/// </summary>
public class TypeMember
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string Accessibility { get; set; }
    public bool IsStatic { get; set; }
    public required Location Location { get; set; }
}

/// <summary>
/// Type structure
/// </summary>
public class TypeStructure
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<TypeMember> Members { get; set; } = new();
}

/// <summary>
/// Type inheritance
/// </summary>
public class TypeInheritance
{
    public required string TypeName { get; set; }
    public string? BaseClass { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<string> DerivedTypes { get; set; } = new();
    public int InheritanceDepth { get; set; }
}

/// <summary>
/// Type usage
/// </summary>
public class TypeUsage
{
    public required string FilePath { get; set; }
    public required string UsageType { get; set; }
    public required Location Location { get; set; }
    public string? Context { get; set; }
}

/// <summary>
/// Type implementation
/// </summary>
public class TypeImplementation
{
    public required string TypeName { get; set; }
    public required string ImplementationType { get; set; }
    public required Location Location { get; set; }
    public List<string> ImplementedMembers { get; set; } = new();
}

/// <summary>
/// Type member analysis
/// </summary>
public class TypeMemberAnalysis
{
    public int TotalMembers { get; set; }
    public int PublicMembers { get; set; }
    public int PrivateMembers { get; set; }
    public int StaticMembers { get; set; }
    public int AbstractMembers { get; set; }
    public List<TypeMember> ComplexMembers { get; set; } = new();
}

/// <summary>
/// Type metrics
/// </summary>
public class TypeMetrics
{
    public int LinesOfCode { get; set; }
    public double CyclomaticComplexity { get; set; }
    public double MaintainabilityIndex { get; set; }
    public int CouplingFactor { get; set; }
    public int ResponseForClass { get; set; }
    public double LackOfCohesion { get; set; }
}
