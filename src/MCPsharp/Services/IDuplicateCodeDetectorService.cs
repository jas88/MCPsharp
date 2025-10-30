using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for detecting duplicate code blocks and providing refactoring suggestions
/// </summary>
public interface IDuplicateCodeDetectorService
{
    /// <summary>
    /// Detect duplicate code blocks in the project
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="options">Options for duplicate detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive duplicate detection results</returns>
    Task<DuplicateDetectionResult> DetectDuplicatesAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find exact duplicate code blocks (100% identical)
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="options">Options for detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of exact duplicate groups</returns>
    Task<IReadOnlyList<DuplicateGroup>> FindExactDuplicatesAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find near-miss duplicate code blocks with configurable similarity threshold
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="similarityThreshold">Similarity threshold (0.0-1.0)</param>
    /// <param name="options">Options for detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of near-miss duplicate groups</returns>
    Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
        string projectPath,
        double similarityThreshold = 0.8,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze duplication metrics and statistics for the project
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="options">Options for analysis</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Duplication metrics and statistics</returns>
    Task<DuplicationMetrics> AnalyzeDuplicationMetricsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get refactoring suggestions for detected duplicate code
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="duplicates">List of duplicate groups to analyze</param>
    /// <param name="options">Options for refactoring suggestions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Refactoring suggestions for the duplicates</returns>
    Task<IReadOnlyList<RefactoringSuggestion>> GetRefactoringSuggestionsAsync(
        string projectPath,
        IReadOnlyList<DuplicateGroup> duplicates,
        RefactoringOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect duplicate code in specific files only
    /// </summary>
    /// <param name="filePaths">List of file paths to analyze</param>
    /// <param name="options">Options for detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Duplicate detection results for specified files</returns>
    Task<DuplicateDetectionResult> DetectDuplicatesInFilesAsync(
        IReadOnlyList<string> filePaths,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two specific code blocks for similarity
    /// </summary>
    /// <param name="codeBlock1">First code block</param>
    /// <param name="codeBlock2">Second code block</param>
    /// <param name="options">Options for comparison</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Similarity analysis result</returns>
    Task<CodeSimilarityResult> CompareCodeBlocksAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a summary of duplicate code hotspots in the project
    /// </summary>
    /// <param name="projectPath">Path to the project to analyze</param>
    /// <param name="options">Options for analysis</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hotspot analysis results</returns>
    Task<DuplicateHotspotsResult> GetDuplicateHotspotsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate potential refactoring by checking if it would break dependencies
    /// </summary>
    /// <param name="projectPath">Path to the project</param>
    /// <param name="suggestion">Refactoring suggestion to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with any issues found</returns>
    Task<RefactoringValidationResult> ValidateRefactoringAsync(
        string projectPath,
        RefactoringSuggestion suggestion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for duplicate code detection
/// </summary>
public record DuplicateDetectionOptions
{
    /// <summary>
    /// Minimum code block size to analyze (in lines)
    /// </summary>
    public int MinBlockSize { get; init; } = 3;

    /// <summary>
    /// Maximum code block size to analyze (in lines)
    /// </summary>
    public int MaxBlockSize { get; init; } = 1000;

    /// <summary>
    /// Types of code elements to analyze
    /// </summary>
    public DuplicateDetectionTypes DetectionTypes { get; init; } =
        DuplicateDetectionTypes.Methods |
        DuplicateDetectionTypes.Classes |
        DuplicateDetectionTypes.CodeBlocks;

    /// <summary>
    /// Files and directories to exclude from analysis
    /// </summary>
    public IReadOnlyList<string> ExcludedPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether to ignore generated code
    /// </summary>
    public bool IgnoreGeneratedCode { get; init; } = true;

    /// <summary>
    /// Whether to ignore test code
    /// </summary>
    public bool IgnoreTestCode { get; init; } = false;

    /// <summary>
    /// Whether to ignore trivial differences (whitespace, comments)
    /// </summary>
    public bool IgnoreTrivialDifferences { get; init; } = true;

    /// <summary>
    /// Whether to ignore variable and method names (structure-only comparison)
    /// </summary>
    public bool IgnoreIdentifiers { get; init; } = false;

    /// <summary>
    /// Similarity threshold for near-miss detection (0.0-1.0)
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.8;

    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int MaxResults { get; init; } = 100;

    /// <summary>
    /// Whether to include context lines in results
    /// </summary>
    public bool IncludeContext { get; init; } = true;

    /// <summary>
    /// Number of context lines to include before/after duplicates
    /// </summary>
    public int ContextLines { get; init; } = 3;

    /// <summary>
    /// Language-specific settings
    /// </summary>
    public Dictionary<string, object> LanguageSettings { get; init; } = new();
}

/// <summary>
/// Types of duplicate code detection
/// </summary>
[Flags]
public enum DuplicateDetectionTypes
{
    /// <summary>
    /// No detection
    /// </summary>
    None = 0,

    /// <summary>
    /// Detect duplicate methods
    /// </summary>
    Methods = 1 << 0,

    /// <summary>
    /// Detect duplicate classes and structs
    /// </summary>
    Classes = 1 << 1,

    /// <summary>
    /// Detect duplicate code blocks within methods
    /// </summary>
    CodeBlocks = 1 << 2,

    /// <summary>
    /// Detect duplicate entire files
    /// </summary>
    Files = 1 << 3,

    /// <summary>
    /// Detect duplicate properties
    /// </summary>
    Properties = 1 << 4,

    /// <summary>
    /// Detect duplicate constructors
    /// </summary>
    Constructors = 1 << 5,

    /// <summary>
    /// All detection types
    /// </summary>
    All = Methods | Classes | CodeBlocks | Files | Properties | Constructors
}

/// <summary>
/// Options for code comparison
/// </summary>
public record CodeComparisonOptions
{
    /// <summary>
    /// Whether to ignore whitespace differences
    /// </summary>
    public bool IgnoreWhitespace { get; init; } = true;

    /// <summary>
    /// Whether to ignore comments
    /// </summary>
    public bool IgnoreComments { get; init; } = true;

    /// <summary>
    /// Whether to ignore identifier names (variables, methods)
    /// </summary>
    public bool IgnoreIdentifiers { get; init; } = false;

    /// <summary>
    /// Whether to ignore string literals
    /// </summary>
    public bool IgnoreStringLiterals { get; init; } = false;

    /// <summary>
    /// Whether to ignore numeric literals
    /// </summary>
    public bool IgnoreNumericLiterals { get; init; } = false;

    /// <summary>
    /// Weight for structural similarity (0.0-1.0)
    /// </summary>
    public double StructuralWeight { get; init; } = 0.7;

    /// <summary>
    /// Weight for token similarity (0.0-1.0)
    /// </summary>
    public double TokenWeight { get; init; } = 0.3;
}

/// <summary>
/// Options for refactoring suggestions
/// </summary>
public record RefactoringOptions
{
    /// <summary>
    /// Types of refactoring to suggest
    /// </summary>
    public RefactoringTypes RefactoringTypes { get; init; } =
        RefactoringTypes.ExtractMethod |
        RefactoringTypes.ExtractClass |
        RefactoringTypes.ExtractBaseClass;

    /// <summary>
    /// Minimum complexity threshold for suggesting refactoring
    /// </summary>
    public int MinComplexityThreshold { get; init; } = 5;

    /// <summary>
    /// Maximum number of suggestions to generate
    /// </summary>
    public int MaxSuggestions { get; init; } = 20;

    /// <summary>
    /// Whether to prioritize breaking changes
    /// </summary>
    public bool PrioritizeBreakingChanges { get; init; } = false;

    /// <summary>
    /// Whether to include estimated effort
    /// </summary>
    public bool IncludeEffortEstimate { get; init; } = true;
}

/// <summary>
/// Types of refactoring suggestions
/// </summary>
[Flags]
public enum RefactoringTypes
{
    /// <summary>
    /// No refactoring
    /// </summary>
    None = 0,

    /// <summary>
    /// Extract common code to method
    /// </summary>
    ExtractMethod = 1 << 0,

    /// <summary>
    /// Extract common code to class
    /// </summary>
    ExtractClass = 1 << 1,

    /// <summary>
    /// Extract common functionality to base class
    /// </summary>
    ExtractBaseClass = 1 << 2,

    /// <summary>
    /// Use template method pattern
    /// </summary>
    TemplateMethod = 1 << 3,

    /// <summary>
    /// Use strategy pattern
    /// </summary>
    StrategyPattern = 1 << 4,

    /// <summary>
    /// Use utility/helper class
    /// </summary>
    UtilityClass = 1 << 5,

    /// <summary>
    /// Use composition over inheritance
    /// </summary>
    Composition = 1 << 6,

    /// <summary>
    /// All refactoring types
    /// </summary>
    All = ExtractMethod | ExtractClass | ExtractBaseClass | TemplateMethod |
          StrategyPattern | UtilityClass | Composition
}