namespace MCPsharp.Models.DuplicateDetection;

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
}
