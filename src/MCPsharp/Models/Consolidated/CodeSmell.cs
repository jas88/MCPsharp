namespace MCPsharp.Models.Consolidated;

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
    public string CodeQuality { get; set; } = "Unknown";
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
