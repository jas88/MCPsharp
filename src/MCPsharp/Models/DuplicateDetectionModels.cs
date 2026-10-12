namespace MCPsharp.Models;

/// <summary>
/// Result of duplicate code detection
/// </summary>
public class DuplicateDetectionResult
{
    /// <summary>
    /// List of duplicate code groups found
    /// </summary>
    public required IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; }

    /// <summary>
    /// Metrics about the duplication analysis
    /// </summary>
    public required DuplicationMetrics Metrics { get; init; }

    /// <summary>
    /// Refactoring suggestions for the duplicates
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion>? RefactoringSuggestions { get; init; }

    /// <summary>
    /// Hotspots analysis
    /// </summary>
    public DuplicateHotspotsResult? Hotspots { get; init; }

    /// <summary>
    /// Analysis duration
    /// </summary>
    public required TimeSpan AnalysisDuration { get; init; }

    /// <summary>
    /// Number of files analyzed
    /// </summary>
    public required int FilesAnalyzed { get; init; }

    /// <summary>
    /// Any warnings or issues during analysis
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Whether the analysis was completed successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if analysis failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Group of duplicate code blocks
/// </summary>
public class DuplicateGroup
{
    /// <summary>
    /// Unique identifier for this duplicate group
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// List of duplicate code blocks in this group
    /// </summary>
    public required IReadOnlyList<CodeBlock> CodeBlocks { get; init; }

    /// <summary>
    /// Similarity score for this group (0.0-1.0, 1.0 = identical)
    /// </summary>
    public required double SimilarityScore { get; init; }

    /// <summary>
    /// Type of duplication
    /// </summary>
    public required DuplicateType DuplicationType { get; init; }

    /// <summary>
    /// Number of lines of duplicated code
    /// </summary>
    public required int LineCount { get; init; }

    /// <summary>
    /// Estimated complexity of the duplicated code
    /// </summary>
    public required int Complexity { get; init; }

    /// <summary>
    /// Whether this is an exact duplicate (100% identical)
    /// </summary>
    public required bool IsExactDuplicate { get; init; }

    /// <summary>
    /// Refactoring suggestions specific to this group
    /// </summary>
    public IReadOnlyList<RefactoringSuggestion>? RefactoringSuggestions { get; init; }

    /// <summary>
    /// Impact assessment for this duplication
    /// </summary>
    public required DuplicationImpact Impact { get; init; }

    /// <summary>
    /// Metadata about the duplication
    /// </summary>
    public required DuplicateMetadata Metadata { get; init; }

    /// <summary>
    /// Clone classification of the group (exact, renamed or near-miss)
    /// </summary>
    public CloneType CloneType { get; init; } = CloneType.Exact;
}

/// <summary>
/// Represents a block of code
/// </summary>
public class CodeBlock
{
    /// <summary>
    /// File path containing this code block
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Starting line number (1-based)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Ending line number (1-based)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Starting column number (1-based)
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Ending column number (1-based)
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// The actual source code
    /// </summary>
    public required string SourceCode { get; init; }

    /// <summary>
    /// Normalized source code for comparison
    /// </summary>
    public required string NormalizedCode { get; init; }

    /// <summary>
    /// Hash of the normalized code for fast comparison
    /// </summary>
    public required string CodeHash { get; init; }

    /// <summary>
    /// Type of code element
    /// </summary>
    public required CodeElementType ElementType { get; init; }

    /// <summary>
    /// Name of the code element (method, class, etc.)
    /// </summary>
    public required string ElementName { get; init; }

    /// <summary>
    /// Containing type/class name
    /// </summary>
    public string? ContainingType { get; init; }

    /// <summary>
    /// Namespace of the code element
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Accessibility level
    /// </summary>
    public required Accessibility Accessibility { get; init; }

    /// <summary>
    /// Whether this code block is generated
    /// </summary>
    public required bool IsGenerated { get; init; }

    /// <summary>
    /// Whether this code block is in a test file
    /// </summary>
    public required bool IsTestCode { get; init; }

    /// <summary>
    /// Complexity metrics for this code block
    /// </summary>
    public required ComplexityMetrics Complexity { get; init; }

    /// <summary>
    /// Tokens extracted from the code block
    /// </summary>
    public IReadOnlyList<CodeToken>? Tokens { get; init; }

    /// <summary>
    /// AST structure information
    /// </summary>
    public required AstStructure AstStructure { get; init; }

    /// <summary>
    /// Context lines before and after the code block
    /// </summary>
    public CodeContext? Context { get; init; }

    /// <summary>
    /// Absolute character offset of the block's syntax node within its file, when known
    /// </summary>
    public int? SpanStart { get; init; }

    /// <summary>
    /// Character length of the block's syntax node within its file, when known
    /// </summary>
    public int? SpanLength { get; init; }

    /// <summary>
    /// Normalised token fingerprint used for Type-2/Type-3 clone detection and diffing
    /// </summary>
    public CloneFingerprint? Fingerprint { get; init; }
}

/// <summary>
/// Context information around a code block
/// </summary>
public class CodeContext
{
    /// <summary>
    /// Lines before the code block
    /// </summary>
    public required IReadOnlyList<string> BeforeLines { get; init; }

    /// <summary>
    /// Lines after the code block
    /// </summary>
    public required IReadOnlyList<string> AfterLines { get; init; }

    /// <summary>
    /// Number of lines before
    /// </summary>
    public required int BeforeLineCount { get; init; }

    /// <summary>
    /// Number of lines after
    /// </summary>
    public required int AfterLineCount { get; init; }
}

/// <summary>
/// Token extracted from source code
/// </summary>
public class CodeToken
{
    /// <summary>
    /// Token type
    /// </summary>
    public required TokenType Type { get; init; }

    /// <summary>
    /// Token text
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Starting position
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// Token length
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Line number
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number
    /// </summary>
    public required int Column { get; init; }
}

/// <summary>
/// AST structure information for code comparison
/// </summary>
public class AstStructure
{
    /// <summary>
    /// Structural hash for fast comparison
    /// </summary>
    public required string StructuralHash { get; init; }

    /// <summary>
    /// Node types in the AST (depth-first order)
    /// </summary>
    public required IReadOnlyList<string> NodeTypes { get; init; }

    /// <summary>
    /// AST depth
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// Number of nodes in the AST
    /// </summary>
    public required int NodeCount { get; init; }

    /// <summary>
    /// Complexity score based on AST structure
    /// </summary>
    public required double StructuralComplexity { get; init; }

    /// <summary>
    /// Control flow patterns identified
    /// </summary>
    public required IReadOnlyList<ControlFlowPattern> ControlFlowPatterns { get; init; }

    /// <summary>
    /// Data flow patterns identified
    /// </summary>
    public required IReadOnlyList<DataFlowPattern> DataFlowPatterns { get; init; }
}

/// <summary>
/// Metadata for duplicate groups
/// </summary>
public class DuplicateMetadata
{
    /// <summary>
    /// When this duplication was first detected
    /// </summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>
    /// Detection algorithm used
    /// </summary>
    public required string DetectionAlgorithm { get; init; }

    /// <summary>
    /// Configuration used for detection
    /// </summary>
    public required string DetectionConfiguration { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public required Dictionary<string, object> AdditionalMetadata { get; init; }
}
