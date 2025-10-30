using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Represents an analyzer rule
/// </summary>
public record AnalyzerRule
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public RuleCategory Category { get; init; }
    public IssueSeverity DefaultSeverity { get; init; }
    public bool IsEnabledByDefault { get; init; } = true;
    public string? HelpLink { get; init; }
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Represents an analyzer issue
/// </summary>
public record AnalyzerIssue
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string RuleId { get; init; } = string.Empty;
    public string AnalyzerId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public int ColumnNumber { get; init; }
    public int EndLineNumber { get; init; }
    public int EndColumnNumber { get; init; }
    public IssueSeverity Severity { get; init; }
    public Confidence Confidence { get; init; }
    public RuleCategory Category { get; init; }
    public ImmutableArray<string> MessageFormat { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableDictionary<string, object> Properties { get; init; } = ImmutableDictionary<string, object>.Empty;
    public string? HelpLink { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a fix for an analyzer issue
/// </summary>
public record AnalyzerFix
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string RuleId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Confidence Confidence { get; init; }
    public bool IsInteractive { get; init; }
    public bool IsBatchable { get; init; } = true;
    public ImmutableArray<TextEdit> Edits { get; init; } = ImmutableArray<TextEdit>.Empty;
    public ImmutableArray<string> RequiredInputs { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> AffectedFiles { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Configuration for an analyzer
/// </summary>
public record AnalyzerConfiguration
{
    public Dictionary<string, object> Properties { get; init; } = new();
    public Dictionary<string, RuleConfiguration> Rules { get; init; } = new();
    public bool IsEnabled { get; init; } = true;
    public ImmutableArray<string> ExcludeFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> IncludeFiles { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Configuration for a specific rule
/// </summary>
public record RuleConfiguration
{
    public bool IsEnabled { get; init; } = true;
    public IssueSeverity? Severity { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
}

/// <summary>
/// Result of analyzing a file
/// </summary>
public record AnalysisResult
{
    public string FilePath { get; init; } = string.Empty;
    public string AnalyzerId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<AnalyzerIssue> Issues { get; init; } = ImmutableArray<AnalyzerIssue>.Empty;
    public ImmutableDictionary<string, object> Statistics { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Result of applying fixes
/// </summary>
public record FixResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string FixId { get; init; } = string.Empty;
    public string AnalyzerId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ImmutableArray<string> ModifiedFiles { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Conflicts { get; init; } = ImmutableArray<string>.Empty;
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;
    public ImmutableDictionary<string, object> Metrics { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Information about an analyzer assembly
/// </summary>
public record AnalyzerInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Version Version { get; init; } = new();
    public string Author { get; init; } = string.Empty;
    public string AssemblyPath { get; init; } = string.Empty;
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
    public bool IsBuiltIn { get; init; }
    public bool IsEnabled { get; init; } = true;
    public ImmutableArray<string> SupportedExtensions { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<AnalyzerRule> Rules { get; init; } = ImmutableArray<AnalyzerRule>.Empty;
    public string? Checksum { get; init; }
    public string? Signature { get; init; }
}

/// <summary>
/// Request to run analysis
/// </summary>
public record AnalysisRequest
{
    public string AnalyzerId { get; init; } = string.Empty;
    public ImmutableArray<string> Files { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> Rules { get; init; } = ImmutableArray<string>.Empty;
    public AnalyzerConfiguration? Configuration { get; init; }
    public bool IncludeDisabledRules { get; init; } = false;
    public bool GenerateFixes { get; init; } = true;
}

/// <summary>
/// Request to apply fixes
/// </summary>
public record ApplyFixRequest
{
    public string AnalyzerId { get; init; } = string.Empty;
    public ImmutableArray<string> IssueIds { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> FixIds { get; init; } = ImmutableArray<string>.Empty;
    public bool PreviewOnly { get; init; } = false;
    public bool ResolveConflicts { get; init; } = true;
    public Dictionary<string, object> Inputs { get; init; } = new();
}