namespace MCPsharp.Models.Consolidated;

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
