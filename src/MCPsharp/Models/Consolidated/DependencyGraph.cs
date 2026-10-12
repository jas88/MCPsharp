namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Dependency graph
/// </summary>
public class DependencyGraph
{
    public List<DependencyNode> Nodes { get; set; } = new();
    public List<DependencyEdge> Edges { get; set; } = new();
}

/// <summary>
/// Dependency node
/// </summary>
public class DependencyNode
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public List<string> Labels { get; set; } = new();
}

/// <summary>
/// Dependency edge
/// </summary>
public class DependencyEdge
{
    public required string From { get; set; }
    public required string To { get; set; }
    public required string Type { get; set; }
    public double Weight { get; set; }
}

/// <summary>
/// Circular dependency
/// </summary>
public class ConsolidatedCircularDependency
{
    public required List<string> Cycle { get; set; }
    public required List<Location> Locations { get; set; }
    public required int Length { get; set; }

    // Additional properties for compatibility with Roslyn.CircularDependency
    public List<MCPsharp.Models.Roslyn.MethodSignature> Methods { get; set; } = new();
    public List<string> FilesInvolved { get; set; } = new();
    public int CycleLength { get; set; }
}

/// <summary>
/// Dependency impact
/// </summary>
public class DependencyImpact
{
    public required string Target { get; set; }
    public List<string> AffectedComponents { get; set; } = new();
    public int TotalImpact { get; set; }
    public List<ImpactDetail> Details { get; set; } = new();
}

/// <summary>
/// Impact detail
/// </summary>
public class ImpactDetail
{
    public required string Component { get; set; }
    public required string ImpactType { get; set; }
    public required int Severity { get; set; }
}

/// <summary>
/// Critical path
/// </summary>
public class CriticalPath
{
    public required List<string> Path { get; set; }
    public required double Criticality { get; set; }
    public required string Description { get; set; }
}

/// <summary>
/// Dependency metrics
/// </summary>
public class DependencyMetrics
{
    public int TotalNodes { get; set; }
    public int TotalEdges { get; set; }
    public double Density { get; set; }
    public int Cycles { get; set; }
    public double AveragePathLength { get; set; }
    public List<NodeMetrics> NodeMetrics { get; set; } = new();
}

/// <summary>
/// Node metrics
/// </summary>
public class NodeMetrics
{
    public required string NodeId { get; set; }
    public int InDegree { get; set; }
    public int OutDegree { get; set; }
    public double Centrality { get; set; }
    public bool IsInCycle { get; set; }
}
