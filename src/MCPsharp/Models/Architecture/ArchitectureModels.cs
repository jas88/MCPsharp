namespace MCPsharp.Models.Architecture;

/// <summary>
/// Definition of architectural layers and rules
/// </summary>
public class ArchitectureDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<ArchitecturalLayer> Layers { get; init; } = new();
    public required List<DependencyRule> DependencyRules { get; init; } = new();
    public required List<NamingPattern> NamingPatterns { get; init; } = new();
    public required List<ForbiddenPattern> ForbiddenPatterns { get; init; } = new();
    public ArchitectureType Type { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Represents an architectural layer in the system
/// </summary>
public class ArchitecturalLayer
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Level { get; init; } // 0 = outermost, higher numbers = inner layers
    public required List<string> NamespacePatterns { get; init; } = new();
    public required List<string> AllowedDependencies { get; init; } = new();
    public required List<string> ForbiddenDependencies { get; init; } = new();
    public LayerType Type { get; init; }
    public bool IsCore { get; init; }
    public List<string> TypicalClasses { get; init; } = new();
}

/// <summary>
/// Defines dependency rules between layers
/// </summary>
public class DependencyRule
{
    public required string FromLayer { get; init; }
    public required string ToLayer { get; init; }
    public DependencyDirection Direction { get; init; }
    public RuleType Type { get; init; }
    public string? Description { get; init; }
    public bool IsStrict { get; init; } = true;
}

/// <summary>
/// Naming pattern for identifying layer membership
/// </summary>
public class NamingPattern
{
    public required string LayerName { get; init; }
    public required string Pattern { get; init; }
    public PatternType Type { get; init; }
    public bool IsRequired { get; init; } = true;
}

/// <summary>
/// Forbidden patterns that violate architectural principles
/// </summary>
public class ForbiddenPattern
{
    public required string Name { get; init; }
    public required string Pattern { get; init; }
    public required string Description { get; init; }
    public PatternType Type { get; init; }
    public ViolationSeverity Severity { get; init; }
}

/// <summary>
/// Result of architecture validation
/// </summary>
public class ArchitectureValidationResult
{
    public required bool IsValid { get; init; }
    public required List<LayerViolation> Violations { get; init; } = new();
    public required List<ArchitectureWarning> Warnings { get; init; } = new();
    public required int TotalTypesAnalyzed { get; init; }
    public required int CompliantTypes { get; init; }
    public required double CompliancePercentage { get; init; }
    public required TimeSpan AnalysisDuration { get; init; }
    public required Dictionary<string, int> LayerStatistics { get; init; } = new();
    public required List<string> AnalyzedFiles { get; init; } = new();
}

/// <summary>
/// Represents a violation of architectural rules
/// </summary>
public class LayerViolation
{
    public required string ViolationType { get; init; }
    public required string Description { get; init; }
    public required string SourceLayer { get; init; }
    public required string TargetLayer { get; init; }
    public required string SourceType { get; init; }
    public required string TargetType { get; init; }
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
    public required int ColumnNumber { get; init; }
    public required ViolationSeverity Severity { get; init; }
    public required string CodeSnippet { get; init; }
    public required List<ArchitectureRecommendation> Recommendations { get; init; } = new();
    public required string RuleViolated { get; init; }
}

/// <summary>
/// Warning about potential architectural issues
/// </summary>
public class ArchitectureWarning
{
    public required string WarningType { get; init; }
    public required string Description { get; init; }
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
    public required string Suggestion { get; init; }
}

/// <summary>
/// Result of dependency analysis
/// </summary>
public class DependencyAnalysisResult
{
    public required List<DependencyNode> Nodes { get; init; } = new();
    public required List<DependencyEdge> Edges { get; init; } = new();
    public required List<CircularDependency> CircularDependencies { get; init; } = new();
    public required Dictionary<string, List<string>> LayerDependencies { get; init; } = new();
    public required Dictionary<string, int> DependencyMetrics { get; init; } = new();
    public required TimeSpan AnalysisDuration { get; init; }
}

/// <summary>
/// Represents a node in the dependency graph
/// </summary>
public class DependencyNode
{
    public required string Name { get; init; }
    public required string Layer { get; init; }
    public required NodeType Type { get; init; }
    public required string FilePath { get; init; }
    public required List<string> OutgoingDependencies { get; init; } = new();
    public required List<string> IncomingDependencies { get; init; } = new();
    public required int Complexity { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Represents an edge in the dependency graph
/// </summary>
public class DependencyEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required DependencyType Type { get; init; }
    public required string FromLayer { get; init; }
    public required string ToLayer { get; init; }
    public required bool IsAllowed { get; init; }
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
}

/// <summary>
/// Represents a circular dependency
/// </summary>
public class CircularDependency
{
    public required List<string> Cycle { get; init; } = new();
    public required List<string> LayersInvolved { get; init; } = new();
    public required int CycleLength { get; init; }
    public required ViolationSeverity Severity { get; init; }
    public required string Description { get; init; }
    public required List<DependencyEdge> Edges { get; init; } = new();
}

/// <summary>
/// Comprehensive architecture report
/// </summary>
public class ArchitectureReport
{
    public required ArchitectureValidationResult ValidationResult { get; init; }
    public required DependencyAnalysisResult DependencyAnalysis { get; init; }
    public required ArchitectureDiagram Diagram { get; init; }
    public required List<ArchitectureRecommendation> Recommendations { get; init; } = new();
    public required ReportSummary Summary { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required string ProjectPath { get; init; }
    public required ArchitectureDefinition ArchitectureUsed { get; init; }
}

/// <summary>
/// Architecture diagram data for visualization
/// </summary>
public class ArchitectureDiagram
{
    public required List<DiagramNode> Nodes { get; init; } = new();
    public required List<DiagramEdge> Edges { get; init; } = new();
    public required List<DiagramLayer> Layers { get; init; } = new();
    public required DiagramLayout Layout { get; init; }
    public required Dictionary<string, string> ColorScheme { get; init; } = new();
}

/// <summary>
/// Node in architecture diagram
/// </summary>
public class DiagramNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Layer { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required string Color { get; init; }
    public required NodeType Type { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// Edge in architecture diagram
/// </summary>
public class DiagramEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required EdgeType Type { get; init; }
    public required string Color { get; init; }
    public required bool IsViolating { get; init; }
    public required List<string> ControlPoints { get; init; } = new();
}

/// <summary>
/// Layer representation in diagram
/// </summary>
public class DiagramLayer
{
    public required string Name { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required string Color { get; init; }
    public required int Level { get; init; }
    public required List<string> NodeIds { get; init; } = new();
}

/// <summary>
/// Layout information for diagram
/// </summary>
public class DiagramLayout
{
    public required LayoutType Type { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// Recommendation for fixing architectural violations
/// </summary>
public class ArchitectureRecommendation
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required RecommendationPriority Priority { get; init; }
    public required List<string> Steps { get; init; } = new();
    public required string? CodeExample { get; init; }
    public required double Effort { get; init; } // 1-10 scale
    public required double Impact { get; init; } // 1-10 scale
}

/// <summary>
/// Result of type compliance check
/// </summary>
public class TypeComplianceResult
{
    public required string TypeName { get; init; }
    public required string Layer { get; init; }
    public required bool IsCompliant { get; init; }
    public required List<string> Violations { get; init; } = new();
    public required List<string> Warnings { get; init; } = new();
    public required List<DependencyInfo> Dependencies { get; init; } = new();
}

/// <summary>
/// Information about a dependency
/// </summary>
public class DependencyInfo
{
    public required string TargetType { get; init; }
    public required string TargetLayer { get; init; }
    public required DependencyType Type { get; init; }
    public required bool IsAllowed { get; init; }
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
}

/// <summary>
/// Summary of architecture report
/// </summary>
public class ReportSummary
{
    public required int TotalViolations { get; init; }
    public required int CriticalViolations { get; init; }
    public required int MajorViolations { get; init; }
    public required int MinorViolations { get; init; }
    public required int TotalWarnings { get; init; }
    public required double OverallCompliance { get; init; }
    public required List<string> TopIssues { get; init; } = new();
    public required Dictionary<string, int> ViolationsByLayer { get; init; } = new();
    public required Dictionary<string, int> ViolationsByType { get; init; } = new();
}