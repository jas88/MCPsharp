namespace MCPsharp.Models;

/// <summary>
/// Clone classification following the usual Type-1/Type-2/Type-3 taxonomy
/// </summary>
public enum CloneType
{
    /// <summary>
    /// Type-1: identical apart from whitespace and comments
    /// </summary>
    Exact,

    /// <summary>
    /// Type-2: identical after consistently renaming identifiers and abstracting literals and types
    /// </summary>
    Renamed,

    /// <summary>
    /// Type-3: similar after normalisation, with added, removed or modified statements
    /// </summary>
    NearMiss
}

/// <summary>
/// Lexical category of a normalised token
/// </summary>
public enum NormalizedTokenKind
{
    /// <summary>
    /// Language keyword (other than predefined type keywords)
    /// </summary>
    Keyword,

    /// <summary>
    /// Identifier referring to a variable, parameter, member or other non-type symbol
    /// </summary>
    Identifier,

    /// <summary>
    /// Type name, including predefined type keywords such as <c>int</c>
    /// </summary>
    TypeName,

    /// <summary>
    /// String, character or interpolated-string text literal
    /// </summary>
    StringLiteral,

    /// <summary>
    /// Numeric literal
    /// </summary>
    NumericLiteral,

    /// <summary>
    /// Operator or punctuation
    /// </summary>
    Punctuation
}

/// <summary>
/// A source token with its normalised forms
/// </summary>
public sealed class NormalizedToken
{
    /// <summary>
    /// Lexical category
    /// </summary>
    public required NormalizedTokenKind Kind { get; init; }

    /// <summary>
    /// Original token text
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Shape form: identifiers, types and literals replaced by category placeholders (used for alignment)
    /// </summary>
    public required string Shape { get; init; }

    /// <summary>
    /// Alpha-renamed form: identifiers replaced by positional placeholders, types and literals abstracted
    /// </summary>
    public required string Renamed { get; init; }

    /// <summary>
    /// Line number (1-based)
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number (1-based)
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// Index of the innermost statement containing this token, or -1
    /// </summary>
    public required int StatementIndex { get; init; }
}

/// <summary>
/// A statement within a fingerprinted code block, expressed as a contiguous token range
/// </summary>
public sealed class StatementSpan
{
    /// <summary>
    /// Statement index within the fingerprint
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Index of the nearest enclosing statement, or -1
    /// </summary>
    public required int ParentIndex { get; init; }

    /// <summary>
    /// Index of the first token of the statement
    /// </summary>
    public required int StartTokenIndex { get; init; }

    /// <summary>
    /// Index of the last token of the statement (inclusive)
    /// </summary>
    public required int EndTokenIndex { get; init; }

    /// <summary>
    /// Line number (1-based)
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number (1-based)
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// Statement source text (possibly truncated)
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Normalised token fingerprint of a code block
/// </summary>
public sealed class CloneFingerprint
{
    /// <summary>
    /// Normalised tokens in source order
    /// </summary>
    public required IReadOnlyList<NormalizedToken> Tokens { get; init; }

    /// <summary>
    /// Statements in source order
    /// </summary>
    public required IReadOnlyList<StatementSpan> Statements { get; init; }

    /// <summary>
    /// Hash of the alpha-renamed token sequence; equal hashes indicate a Type-2 clone
    /// </summary>
    public required string RenamedHash { get; init; }

    /// <summary>
    /// Whether a semantic model was used to classify identifiers
    /// </summary>
    public required bool UsedSemanticModel { get; init; }
}

/// <summary>
/// How an extraction analysis was performed
/// </summary>
public enum ExtractionAnalysisMode
{
    /// <summary>
    /// Roslyn data-flow and control-flow analysis using a semantic model
    /// </summary>
    Semantic,

    /// <summary>
    /// Syntax-only heuristics (no semantic model available)
    /// </summary>
    SyntaxOnly,

    /// <summary>
    /// The code element cannot be extracted into a method (e.g. a class or property)
    /// </summary>
    NotApplicable
}

/// <summary>
/// A variable crossing the boundary of an extracted region
/// </summary>
public sealed class ExtractionVariable
{
    /// <summary>
    /// Variable name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Type display name, when known
    /// </summary>
    public string? Type { get; init; }
}

/// <summary>
/// Dependency analysis of extracting a single code block into a shared method
/// </summary>
public sealed class BlockExtractionAnalysis
{
    /// <summary>
    /// File containing the block
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Starting line of the block (1-based)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Analysis mode used
    /// </summary>
    public required ExtractionAnalysisMode Mode { get; init; }

    /// <summary>
    /// Whether the block can be extracted without changing behaviour
    /// </summary>
    public required bool IsSafe { get; init; }

    /// <summary>
    /// Values flowing into the block, which become parameters of the extracted method
    /// </summary>
    public required IReadOnlyList<ExtractionVariable> Parameters { get; init; }

    /// <summary>
    /// Variables written inside the block and read afterwards, which must be returned
    /// </summary>
    public required IReadOnlyList<ExtractionVariable> ReturnValues { get; init; }

    /// <summary>
    /// Outer variables captured by lambdas or local functions inside the block
    /// </summary>
    public required IReadOnlyList<string> CapturedVariables { get; init; }

    /// <summary>
    /// Private or protected members of the containing type referenced by the block
    /// </summary>
    public required IReadOnlyList<string> ReferencedNonPublicMembers { get; init; }

    /// <summary>
    /// Whether the block uses instance state (so the extracted method cannot be static)
    /// </summary>
    public required bool RequiresInstanceAccess { get; init; }

    /// <summary>
    /// Whether the block contains a return statement
    /// </summary>
    public required bool ContainsReturnStatement { get; init; }

    /// <summary>
    /// Whether the block contains break/continue/goto statements that leave the block
    /// </summary>
    public required bool ContainsJumpOutOfBlock { get; init; }

    /// <summary>
    /// Reasons the extraction is unsafe
    /// </summary>
    public required IReadOnlyList<string> UnsafeReasons { get; init; }

    /// <summary>
    /// Non-blocking concerns
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Suggested signature for the extracted method
    /// </summary>
    public string? SuggestedSignature { get; init; }
}

/// <summary>
/// Aggregated extraction safety for a duplicate group
/// </summary>
public sealed class ExtractionSafetyResult
{
    /// <summary>
    /// Whether every analysed block can be extracted safely into a shared method
    /// </summary>
    public required bool IsSafe { get; init; }

    /// <summary>
    /// Reasons the extraction is unsafe
    /// </summary>
    public required IReadOnlyList<string> UnsafeReasons { get; init; }

    /// <summary>
    /// Non-blocking concerns
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Per-block analyses
    /// </summary>
    public required IReadOnlyList<BlockExtractionAnalysis> Blocks { get; init; }
}
