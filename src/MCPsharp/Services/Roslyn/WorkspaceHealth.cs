using System.Collections.Generic;
using System.Linq;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Tracks the health and capability status of the Roslyn workspace at a granular level.
/// Enables progressive enhancement: text operations always work, syntax operations work
/// on parseable files, semantic operations require clean build.
/// </summary>
public class WorkspaceHealth
{
    /// <summary>
    /// Whether the workspace has been initialized (project opened).
    /// </summary>
    public bool IsInitialized { get; set; }
    
    /// <summary>
    /// Total number of projects in the workspace.
    /// </summary>
    public int TotalProjects { get; set; }
    
    /// <summary>
    /// Number of projects successfully loaded.
    /// </summary>
    public int LoadedProjects { get; set; }
    
    /// <summary>
    /// Total number of source files in the project.
    /// </summary>
    public int TotalFiles { get; set; }
    
    /// <summary>
    /// Number of files that can be parsed (valid C# syntax).
    /// </summary>
    public int ParseableFiles { get; set; }
    
    /// <summary>
    /// Total compilation error count across all projects.
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// Total compilation warning count across all projects.
    /// </summary>
    public int WarningCount { get; set; }
    
    /// <summary>
    /// Per-file health status.
    /// </summary>
    public Dictionary<string, FileHealth> FileStatus { get; set; } = new();
    
    /// <summary>
    /// Level 0: Text operations (read, write, regex search) - always available.
    /// </summary>
    public bool CanDoTextOperations => IsInitialized;
    
    /// <summary>
    /// Level 1: Syntax operations (parse single files, extract structure) - requires parseable files.
    /// </summary>
    public bool CanDoSyntaxOperations => IsInitialized && ParseableFiles > 0;
    
    /// <summary>
    /// Level 2: Semantic operations (cross-file references, type resolution) - requires clean build.
    /// </summary>
    public bool CanDoSemanticOperations => IsInitialized && ErrorCount == 0;
    
    /// <summary>
    /// Level 3: Advanced features (architecture validation, code smells) - requires clean build + full analysis.
    /// </summary>
    public bool CanDoAdvancedOperations => CanDoSemanticOperations && LoadedProjects == TotalProjects;
    
    /// <summary>
    /// Gets a human-readable capability report for Claude Code.
    /// </summary>
    public string GetCapabilityReport()
    {
        var report = $@"
MCPsharp Workspace Capabilities:
✅ Text operations: Always available
{(CanDoSyntaxOperations ? "✅" : "❌")} Syntax operations: {ParseableFiles}/{TotalFiles} files parseable
{(CanDoSemanticOperations ? "✅" : "⚠️")} Semantic operations: {(ErrorCount == 0 ? "Available" : $"{ErrorCount} build errors")}
{(CanDoAdvancedOperations ? "✅" : "❌")} Advanced features: {(CanDoAdvancedOperations ? "Available" : "Requires clean build")}
";
        
        if (ErrorCount > 0)
        {
            report += $"\n💡 Fix {ErrorCount} build errors to enable full semantic features\n";
        }
        
        if (WarningCount > 0)
        {
            report += $"⚠️ {WarningCount} warnings present\n";
        }
        
        return report;
    }
    
    /// <summary>
    /// Gets a JSON-serializable capability summary.
    /// </summary>
    public object GetCapabilitySummary()
    {
        return new
        {
            initialized = IsInitialized,
            capabilities = new
            {
                text = CanDoTextOperations,
                syntax = CanDoSyntaxOperations,
                semantic = CanDoSemanticOperations,
                advanced = CanDoAdvancedOperations
            },
            metrics = new
            {
                projects = new { total = TotalProjects, loaded = LoadedProjects },
                files = new { total = TotalFiles, parseable = ParseableFiles },
                issues = new { errors = ErrorCount, warnings = WarningCount }
            },
            reason = GetLimitationReason()
        };
    }
    
    private string? GetLimitationReason()
    {
        if (!IsInitialized)
            return "Workspace not initialized";
        if (ErrorCount > 0)
            return $"Project has {ErrorCount} build errors";
        if (ParseableFiles < TotalFiles)
            return $"{TotalFiles - ParseableFiles} files cannot be parsed";
        if (LoadedProjects < TotalProjects)
            return $"{TotalProjects - LoadedProjects} projects failed to load";
        return null;
    }
}

/// <summary>
/// Health status for an individual file.
/// </summary>
public class FileHealth
{
    /// <summary>
    /// File path relative to project root.
    /// </summary>
    public string FilePath { get; set; } = "";
    
    /// <summary>
    /// Whether the file can be parsed (valid syntax).
    /// </summary>
    public bool IsParseable { get; set; }
    
    /// <summary>
    /// Number of syntax errors in the file.
    /// </summary>
    public int SyntaxErrors { get; set; }
    
    /// <summary>
    /// Number of semantic errors (requires compilation).
    /// </summary>
    public int SemanticErrors { get; set; }
    
    /// <summary>
    /// Parse error message if file is not parseable.
    /// </summary>
    public string? ParseError { get; set; }
    
    /// <summary>
    /// Timestamp of last analysis.
    /// </summary>
    public DateTimeOffset LastAnalyzed { get; set; }
}
