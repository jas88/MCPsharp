namespace MCPsharp.Models.DuplicateDetection;

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
