using System.Collections.Immutable;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Interface for code analyzers
/// </summary>
public interface IAnalyzer
{
    /// <summary>
    /// Unique identifier for the analyzer
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name for the analyzer
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the analyzer does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Version of the analyzer
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Author of the analyzer
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Supported file extensions
    /// </summary>
    ImmutableArray<string> SupportedExtensions { get; }

    /// <summary>
    /// Whether this analyzer is enabled
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Configuration for the analyzer
    /// </summary>
    AnalyzerConfiguration Configuration { get; set; }

    /// <summary>
    /// Check if the analyzer can analyze the specified target path
    /// </summary>
    bool CanAnalyze(string targetPath);

    /// <summary>
    /// Initialize the analyzer
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze a file and return issues
    /// </summary>
    Task<AnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available rules for this analyzer
    /// </summary>
    ImmutableArray<AnalyzerRule> GetRules();

    /// <summary>
    /// Get available fixes for a specific rule
    /// </summary>
    ImmutableArray<AnalyzerFix> GetFixes(string ruleId);

    /// <summary>
    /// Get capabilities of this analyzer
    /// </summary>
    AnalyzerCapabilities GetCapabilities();

    /// <summary>
    /// Cleanup resources
    /// </summary>
    Task DisposeAsync();
}