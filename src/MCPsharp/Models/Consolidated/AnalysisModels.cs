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
