using Microsoft.CodeAnalysis;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing and optimizing large C# files to improve code quality and maintainability
/// </summary>
public interface ILargeFileOptimizerService
{
    /// <summary>
    /// Analyze a project to identify files that are too large and need optimization
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="maxLines">Maximum number of lines before considering a file large (default: 500)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis result with large files and recommendations</returns>
    Task<LargeFileAnalysisResult> AnalyzeLargeFilesAsync(string projectPath, int? maxLines = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimize a large class by suggesting splitting strategies
    /// </summary>
    /// <param name="filePath">Path to the file containing the large class</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Class optimization strategies with specific recommendations</returns>
    Task<ClassOptimizationResult> OptimizeLargeClassAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimize a large method by suggesting refactoring approaches
    /// </summary>
    /// <param name="filePath">Path to the file containing the method</param>
    /// <param name="methodName">Name of the method to optimize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Method optimization strategies with specific recommendations</returns>
    Task<MethodOptimizationResult> OptimizeLargeMethodAsync(string filePath, string methodName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate complexity metrics for a file or specific method
    /// </summary>
    /// <param name="filePath">Path to the file to analyze</param>
    /// <param name="methodName">Optional method name to focus analysis on</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complexity metrics including cyclomatic and cognitive complexity</returns>
    Task<ComplexityMetrics> GetComplexityMetricsAsync(string filePath, string? methodName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a comprehensive optimization plan for a file
    /// </summary>
    /// <param name="filePath">Path to the file to create optimization plan for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed optimization plan with prioritized actions and estimated effort</returns>
    Task<OptimizationPlan> GenerateOptimizationPlanAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect God classes in a project or specific file
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="filePath">Optional specific file to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of detected God classes with analysis</returns>
    Task<List<GodClassAnalysis>> DetectGodClassesAsync(string projectPath, string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect God methods in a project or specific file
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="filePath">Optional specific file to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of detected God methods with analysis</returns>
    Task<List<GodMethodAnalysis>> DetectGodMethodsAsync(string projectPath, string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze code smells in a file that indicate optimization opportunities
    /// </summary>
    /// <param name="filePath">Path to the file to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of detected code smells with severity and fix recommendations</returns>
    Task<List<CodeSmell>> AnalyzeCodeSmellsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggest refactoring patterns for specific code issues
    /// </summary>
    /// <param name="filePath">Path to the file containing the issues</param>
    /// <param name="issues">List of issues to address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Refactoring suggestions with before/after examples</returns>
    Task<List<RefactoringSuggestion>> SuggestRefactoringPatternsAsync(string filePath, List<CodeSmell> issues, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the effort and impact of proposed optimizations
    /// </summary>
    /// <param name="optimizationPlan">The optimization plan to estimate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Effort and impact estimates</returns>
    Task<OptimizationEstimate> EstimateOptimizationEffortAsync(OptimizationPlan optimizationPlan, CancellationToken cancellationToken = default);
}