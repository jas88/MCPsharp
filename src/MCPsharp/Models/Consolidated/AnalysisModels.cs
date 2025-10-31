namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Analysis scope for operations
/// </summary>
public enum AnalysisScope
{
    Definition,
    References,
    Dependencies,
    All
}

/// <summary>
/// File analysis types
/// </summary>
public enum FileAnalysisType
{
    Structure,
    Complexity,
    Dependencies,
    Quality,
    All
}

/// <summary>
/// Project analysis types
/// </summary>
public enum ProjectAnalysisType
{
    Structure,
    Dependencies,
    Build,
    Metrics,
    All
}

/// <summary>
/// Quality metrics
/// </summary>
public enum AnalysisQualityMetric
{
    Duplicates,
    Complexity,
    Maintainability,
    TestCoverage,
    CodeSmells,
    All
}

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

/// <summary>
/// File basic info
/// </summary>
public class FileBasicInfo
{
    public required string FilePath { get; set; }
    public required string Language { get; set; }
    public int LineCount { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public List<string> Namespaces { get; set; } = new();
    public List<string> Types { get; set; } = new();
}

/// <summary>
/// File structure
/// </summary>
public class FileStructure
{
    public List<string> Namespaces { get; set; } = new();
    public List<FileTypeDefinition> Types { get; set; } = new();
    public List<FileMemberDefinition> Members { get; set; } = new();
    public List<FileImport> Imports { get; set; } = new();
}

/// <summary>
/// File type definition
/// </summary>
public class FileTypeDefinition
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required Location Location { get; set; }
    public string? BaseClass { get; set; }
    public List<string> Interfaces { get; set; } = new();
}

/// <summary>
/// File member definition
/// </summary>
public class FileMemberDefinition
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required Location Location { get; set; }
    public required string ContainingType { get; set; }
}

/// <summary>
/// File import
/// </summary>
public class FileImport
{
    public required string ImportPath { get; set; }
    public required string ImportType { get; set; }
    public required Location Location { get; set; }
    public bool IsUsed { get; set; }
}

/// <summary>
/// File complexity
/// </summary>
public class FileComplexity
{
    public required string FilePath { get; set; }
    public double CyclomaticComplexity { get; set; }
    public double CognitiveComplexity { get; set; }
    public int LinesOfCode { get; set; }
    public List<MethodComplexity> MethodComplexities { get; set; } = new();
}

/// <summary>
/// Method complexity
/// </summary>
public class MethodComplexity
{
    public required string MethodName { get; set; }
    public required Location Location { get; set; }
    public int CyclomaticComplexity { get; set; }
    public int CognitiveComplexity { get; set; }
    public int LinesOfCode { get; set; }
}

/// <summary>
/// File dependencies
/// </summary>
public class FileDependencies
{
    public required string FilePath { get; set; }
    public List<FileDependency> InternalDependencies { get; set; } = new();
    public List<FileDependency> ExternalDependencies { get; set; } = new();
    public List<FileDependency> Dependents { get; set; } = new();
}

/// <summary>
/// File dependency
/// </summary>
public class FileDependency
{
    public required string DependencyPath { get; set; }
    public required string DependencyType { get; set; }
    public List<Location> Locations { get; set; } = new();
    public double Strength { get; set; }
}

/// <summary>
/// File quality
/// </summary>
public class FileQuality
{
    public required string FilePath { get; set; }
    public double QualityScore { get; set; }
    public List<QualityIssue> Issues { get; set; } = new();
    public List<QualityMetric> Metrics { get; set; } = new();
}

/// <summary>
/// Quality issue
/// </summary>
public class QualityIssue
{
    public required string Type { get; set; }
    public required string Severity { get; set; }
    public required string Description { get; set; }
    public required Location Location { get; set; }
    public string? Suggestion { get; set; }
}

/// <summary>
/// Quality metric
/// </summary>
public class QualityMetric
{
    public required string Name { get; set; }
    public required double Value { get; set; }
    public required string Unit { get; set; }
    public double? Threshold { get; set; }
}

/// <summary>
/// File issue
/// </summary>
public class FileIssue
{
    public required string Type { get; set; }
    public required string Severity { get; set; }
    public required string Message { get; set; }
    public required Location Location { get; set; }
    public string? Suggestion { get; set; }
}

/// <summary>
/// File suggestion
/// </summary>
public class FileSuggestion
{
    public required string Type { get; set; }
    public required string Description { get; set; }
    public required Location Location { get; set; }
    public double Priority { get; set; }
    public string? CodeExample { get; set; }
}

/// <summary>
/// Project structure
/// </summary>
public class ProjectStructure
{
    public required string ProjectPath { get; set; }
    public required string ProjectType { get; set; }
    public List<string> Configurations { get; set; } = new();
    public List<string> TargetFrameworks { get; set; } = new();
    public List<ProjectReference> References { get; set; } = new();
}

/// <summary>
/// Project reference
/// </summary>
public class ProjectReference
{
    public required string Name { get; set; }
    public required string ReferenceType { get; set; }
    public string? Version { get; set; }
    public required string Path { get; set; }
}

/// <summary>
/// Project file
/// </summary>
public class ProjectFile
{
    public required string Path { get; set; }
    public required string Type { get; set; }
    public required string Language { get; set; }
    public long Size { get; set; }
    public int LineCount { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Project dependencies
/// </summary>
public class ProjectDependencies
{
    public List<PackageDependency> PackageReferences { get; set; } = new();
    public List<ProjectReference> ProjectReferences { get; set; } = new();
    public List<AssemblyReference> AssemblyReferences { get; set; } = new();
}

/// <summary>
/// Package dependency
/// </summary>
public class PackageDependency
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public bool IsDevelopmentDependency { get; set; }
    public List<string> UsedBy { get; set; } = new();
}

/// <summary>
/// Assembly reference
/// </summary>
public class AssemblyReference
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public string? Version { get; set; }
    public bool IsGac { get; set; }
}

/// <summary>
/// Project build info
/// </summary>
public class ProjectBuildInfo
{
    public required string BuildSystem { get; set; }
    public List<string> BuildConfigurations { get; set; } = new();
    public List<BuildStep> BuildSteps { get; set; } = new();
    public List<string> BuildArtifacts { get; set; } = new();
}

/// <summary>
/// Build step
/// </summary>
public class BuildStep
{
    public required string Name { get; set; }
    public required string Command { get; set; }
    public List<string> Inputs { get; set; } = new();
    public List<string> Outputs { get; set; } = new();
}

/// <summary>
/// Project metrics
/// </summary>
public class ProjectMetrics
{
    public int TotalFiles { get; set; }
    public int LinesOfCode { get; set; }
    public int TypesCount { get; set; }
    public int MethodsCount { get; set; }
    public double AverageComplexity { get; set; }
    public List<FileMetric> FileMetrics { get; set; } = new();
}

/// <summary>
/// File metric
/// </summary>
public class FileMetric
{
    public required string FilePath { get; set; }
    public required int LinesOfCode { get; set; }
    public required double Complexity { get; set; }
    public required double QualityScore { get; set; }
}

/// <summary>
/// Architecture definition
/// </summary>
public class ArchitectureDefinition
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<ArchitectureLayer> Layers { get; set; } = new();
    public List<DependencyRule> Rules { get; set; } = new();

    // Additional properties for compatibility with Models.Architecture.ArchitectureDefinition
    public List<DependencyRule> DependencyRules { get; set; } = new();
    public List<object> NamingPatterns { get; set; } = new();
    public List<object> ForbiddenPatterns { get; set; } = new();
    public string Type { get; set; } = "Custom";
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Architecture layer
/// </summary>
public class ArchitectureLayer
{
    public required string Name { get; set; }
    public required int Level { get; set; }
    public List<string> NamespacePatterns { get; set; } = new();
    public List<string> Types { get; set; } = new();

    // Additional properties for compatibility with Models.Architecture.ArchitecturalLayer
    public string? Description { get; set; }
    public int Order { get; set; }
    public List<string> AllowedDependencies { get; set; } = new();
    public List<string> ForbiddenDependencies { get; set; } = new();
    public string Type { get; set; } = "Standard";
    public bool IsCore { get; set; }
    public List<string> TypicalClasses { get; set; } = new();
}

/// <summary>
/// Dependency rule
/// </summary>
public class DependencyRule
{
    public required string FromLayer { get; set; }
    public required string ToLayer { get; set; }
    public required bool Allowed { get; set; }
    public string? Reason { get; set; }

    // Additional properties for compatibility with Models.Architecture.DependencyRule
    public MCPsharp.Models.Architecture.DependencyDirection Direction { get; set; } = MCPsharp.Models.Architecture.DependencyDirection.Downward;
    public MCPsharp.Models.Architecture.RuleType Type { get; set; } = MCPsharp.Models.Architecture.RuleType.Forbidden;
    public string? Description { get; set; }
    public bool IsStrict { get; set; } = true;
}

/// <summary>
/// Architecture validation result
/// </summary>
public class ArchitectureValidation
{
    public bool IsValid { get; set; }
    public List<ArchitectureViolation> Violations { get; set; } = new();
    public List<ArchitectureWarning> Warnings { get; set; } = new();
}

/// <summary>
/// Architecture violation
/// </summary>
public class ArchitectureViolation
{
    public required string From { get; set; }
    public required string To { get; set; }
    public required string Rule { get; set; }
    public required Location Location { get; set; }
    public required string Description { get; set; }

    // Additional properties for compatibility with Models.Architecture.LayerViolation
    public string? SourceComponent { get; set; }
    public string? TargetComponent { get; set; }
    public string? RuleName { get; set; }
    public int Line { get; set; }
}

/// <summary>
/// Architecture warning
/// </summary>
public class ArchitectureWarning
{
    public required string Type { get; set; }
    public required string Message { get; set; }
    public required Location Location { get; set; }
}

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

/// <summary>
/// Code smell
/// </summary>
public class CodeSmell
{
    public required string Type { get; set; }
    public required string Severity { get; set; }
    public required string Description { get; set; }
    public required MCPsharp.Models.Consolidated.Location Location { get; set; }
    public string? Refactoring { get; set; }
    public string? Suggestion { get; set; } // Additional property for compatibility
}

/// <summary>
/// Quality score
/// </summary>
public class QualityScore
{
    public required double OverallScore { get; set; }
    public required Dictionary<string, double> CategoryScores { get; set; }
    public required string Grade { get; set; }
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
}

/// <summary>
/// Quality recommendation
/// </summary>
public class QualityRecommendation
{
    public required string Type { get; set; }
    public required string Description { get; set; }
    public required double Priority { get; set; }
    public required double Impact { get; set; }
    public required List<Location> Locations { get; set; }
    public string? Example { get; set; }
}

// Request/Response Models

/// <summary>
/// Symbol analysis request
/// </summary>
public class SymbolAnalysisRequest
{
    public required string SymbolName { get; set; }
    public string? Context { get; set; }
    public AnalysisScope Scope { get; set; } = AnalysisScope.All;
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// Symbol analysis response
/// </summary>
public class SymbolAnalysisResponse
{
    public required string SymbolName { get; set; }
    public string? Context { get; set; }
    public SymbolDefinition? Definition { get; set; }
    public List<SymbolReference>? References { get; set; }
    public List<SymbolCaller>? Callers { get; set; }
    public SymbolUsagePatterns? UsagePatterns { get; set; }
    public List<RelatedSymbol>? RelatedSymbols { get; set; }
    public SymbolMetrics? Metrics { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Type analysis request
/// </summary>
public class TypeAnalysisRequest
{
    public required string TypeName { get; set; }
    public string? Context { get; set; }
    public AnalysisScope Scope { get; set; } = AnalysisScope.All;
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// Type analysis response
/// </summary>
public class TypeAnalysisResponse
{
    public required string TypeName { get; set; }
    public string? Context { get; set; }
    public TypeStructure? Structure { get; set; }
    public TypeInheritance? Inheritance { get; set; }
    public List<TypeUsage>? Usages { get; set; }
    public List<TypeImplementation>? Implementations { get; set; }
    public TypeMemberAnalysis? Members { get; set; }
    public TypeMetrics? Metrics { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// File analysis request
/// </summary>
public class FileAnalysisRequest
{
    public required string FilePath { get; set; }
    public List<FileAnalysisType> AnalysisTypes { get; set; } = new();
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // Additional properties for compatibility
    public bool IncludeStructure { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
}

/// <summary>
/// File analysis response
/// </summary>
public class FileAnalysisResponse
{
    public required string FilePath { get; set; }
    public FileBasicInfo? Info { get; set; }
    public FileStructure? Structure { get; set; }
    public FileComplexity? Complexity { get; set; }
    public FileDependencies? Dependencies { get; set; }
    public FileQuality? Quality { get; set; }
    public List<FileIssue>? Issues { get; set; }
    public List<FileSuggestion>? Suggestions { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Project analysis request
/// </summary>
public class ProjectAnalysisRequest
{
    public required string ProjectPath { get; set; }
    public List<ProjectAnalysisType> AnalysisTypes { get; set; } = new();
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // Additional properties for compatibility
    public bool IncludeDependencies { get; set; } = true;
    public bool IncludeMetrics { get; set; } = true;
}

/// <summary>
/// Project analysis response
/// </summary>
public class ProjectAnalysisResponse
{
    public required string ProjectPath { get; set; }
    public ProjectStructure? Structure { get; set; }
    public List<ProjectFile>? Files { get; set; }
    public ProjectDependencies? Dependencies { get; set; }
    public ProjectBuildInfo? BuildInfo { get; set; }
    public ProjectMetrics? Metrics { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Architecture analysis request
/// </summary>
public class ArchitectureAnalysisRequest
{
    public required AnalysisScope Scope { get; set; }
    public ArchitectureDefinition? Definition { get; set; }
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // Additional properties for compatibility
    public string? ProjectPath { get; set; }
    public bool IncludeViolations { get; set; } = true;
    public bool IncludeRecommendations { get; set; } = true;

    // Legacy compatibility - allow string scope
    public string? ScopeString { get; set; } // For string to enum conversion
}

/// <summary>
/// Architecture analysis response
/// </summary>
public class ArchitectureAnalysisResponse
{
    public required string Scope { get; set; }
    public ArchitectureValidation? Validation { get; set; }
    public List<ArchitectureViolation>? Violations { get; set; }
    public DependencyAnalysisResponse? DependencyAnalysis { get; set; }
    public List<ArchitectureRecommendation>? Recommendations { get; set; }
    public ArchitectureMetrics? Metrics { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Architecture recommendation
/// </summary>
public class ArchitectureRecommendation
{
    public required string Type { get; set; }
    public required string Description { get; set; }
    public required string Target { get; set; }
    public required double Priority { get; set; }
    public List<string> AffectedComponents { get; set; } = new();
}

/// <summary>
/// Architecture metrics
/// </summary>
public class ArchitectureMetrics
{
    public int TotalLayers { get; set; }
    public int TotalViolations { get; set; }
    public double Compliance { get; set; }
    public Dictionary<string, int> LayerMetrics { get; set; } = new();
}

/// <summary>
/// Dependency analysis request
/// </summary>
public class DependencyAnalysisRequest
{
    public required AnalysisScope Scope { get; set; }
    public int Depth { get; set; } = 2;
    public bool IncludeCircular { get; set; } = true;
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // Additional properties for compatibility
    public string? TypeName { get; set; }
    public string? ProjectPath { get; set; }

    // Legacy compatibility - allow string scope
    public string? ScopeString { get; set; } // For string to enum conversion
}

/// <summary>
/// Dependency analysis response
/// </summary>
public class DependencyAnalysisResponse
{
    public required string Scope { get; set; }
    public int Depth { get; set; }
    public DependencyGraph? Graph { get; set; }
    public List<ConsolidatedCircularDependency>? CircularDependencies { get; set; }
    public DependencyImpact? ImpactAnalysis { get; set; }
    public List<CriticalPath>? CriticalPaths { get; set; }
    public DependencyMetrics? Metrics { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Quality analysis request
/// </summary>
public class QualityAnalysisRequest
{
    public required AnalysisScope Scope { get; set; }
    public List<AnalysisQualityMetric> Metrics { get; set; } = new();
    public ToolOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // Additional properties for compatibility
    public string? FilePath { get; set; }
    public string? ProjectPath { get; set; }
    public bool IncludeMetrics { get; set; } = true;

    // Legacy compatibility - allow string scope
    public string? ScopeString { get; set; } // For string to enum conversion
}

/// <summary>
/// Quality analysis response
/// </summary>
public class QualityAnalysisResponse
{
    public required string Scope { get; set; }
    public List<DuplicateGroup>? Duplicates { get; set; }
    public ComplexityAnalysis? Complexity { get; set; }
    public MaintainabilityAnalysis? Maintainability { get; set; }
    public TestCoverageAnalysis? TestCoverage { get; set; }
    public List<CodeSmell>? CodeSmells { get; set; }
    public QualityScore? QualityScore { get; set; }
    public List<QualityRecommendation>? Recommendations { get; set; }
    public required ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// Duplicate group
/// </summary>
public class DuplicateGroup
{
    public required string Type { get; set; }
    public required double Similarity { get; set; }
    public required List<DuplicateFile> Files { get; set; }
    public required string Description { get; set; }
}

/// <summary>
/// Duplicate file
/// </summary>
public class DuplicateFile
{
    public required string FilePath { get; set; }
    public required List<Location> Locations { get; set; }
    public required int Lines { get; set; }
    public required double MatchPercentage { get; set; }
}

/// <summary>
/// Complexity analysis
/// </summary>
public class ComplexityAnalysis
{
    public required double AverageComplexity { get; set; }
    public required int TotalMethods { get; set; }
    public required List<ComplexityIssue> ComplexMethods { get; set; }

    // Additional properties for compatibility
    public string Scope { get; set; } = "Project";
    public double TotalComplexity { get; set; }
    public double MaxComplexity { get; set; }
    public int FileCount { get; set; }
    public List<string> ComplexFiles { get; set; } = new();
}

/// <summary>
/// Complexity issue
/// </summary>
public class ComplexityIssue
{
    public required string FilePath { get; set; }
    public required string MethodName { get; set; }
    public required int Complexity { get; set; }
    public required Location Location { get; set; }
}

/// <summary>
/// Maintainability analysis
/// </summary>
public class MaintainabilityAnalysis
{
    public required double AverageMaintainabilityIndex { get; set; }
    public required List<MaintainabilityIssue> Issues { get; set; }

    // Additional properties for compatibility
    public string Scope { get; set; } = "Project";
    public double MaintainabilityIndex { get; set; }
    public double TechnicalDebt { get; set; }
    public string CodeQuality { get; set; }
    public double RefactoringPriority { get; set; }
}

/// <summary>
/// Maintainability issue
/// </summary>
public class MaintainabilityIssue
{
    public required string FilePath { get; set; }
    public required string Component { get; set; }
    public required double MaintainabilityIndex { get; set; }
    public required Location Location { get; set; }
}

/// <summary>
/// Test coverage analysis
/// </summary>
public class TestCoverageAnalysis
{
    public required double OverallCoverage { get; set; }
    public required List<CoverageReport> Reports { get; set; }

    // Additional properties for compatibility
    public string Scope { get; set; } = "Project";
    public int TotalFiles { get; set; }
    public int TestFiles { get; set; }
    public int SourceFiles { get; set; }
    public double CoveragePercentage { get; set; }
    public string CoverageLevel { get; set; } = "Unknown";
}

/// <summary>
/// Coverage report
/// </summary>
public class CoverageReport
{
    public required string FilePath { get; set; }
    public required double LineCoverage { get; set; }
    public required double BranchCoverage { get; set; }
    public required List<UncoveredLine> UncoveredLines { get; set; }
}

/// <summary>
/// Uncovered line
/// </summary>
public class UncoveredLine
{
    public required int LineNumber { get; set; }
    public required string Code { get; set; }
}