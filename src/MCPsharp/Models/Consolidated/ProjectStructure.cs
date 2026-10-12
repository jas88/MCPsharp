namespace MCPsharp.Models.Consolidated;

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
