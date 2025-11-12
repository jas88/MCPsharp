namespace MCPsharp.Models.DuplicateDetection;

/// <summary>
/// Result of code similarity comparison
/// </summary>
public class CodeSimilarityResult
{
    /// <summary>
    /// Overall similarity score (0.0-1.0)
    /// </summary>
    public required double OverallSimilarity { get; init; }

    /// <summary>
    /// Structural similarity score
    /// </summary>
    public required double StructuralSimilarity { get; init; }

    /// <summary>
    /// Token similarity score
    /// </summary>
    public required double TokenSimilarity { get; init; }

    /// <summary>
    /// Semantic similarity score
    /// </summary>
    public required double SemanticSimilarity { get; init; }

    /// <summary>
    /// Differences between the code blocks
    /// </summary>
    public required IReadOnlyList<CodeDifference> Differences { get; init; }

    /// <summary>
    /// Common patterns found
    /// </summary>
    public required IReadOnlyList<CommonPattern> CommonPatterns { get; init; }

    /// <summary>
    /// Whether the code blocks are considered duplicates
    /// </summary>
    public required bool IsDuplicate { get; init; }

    /// <summary>
    /// Duplicate type if they are duplicates
    /// </summary>
    public DuplicateType? DuplicateType { get; init; }
}

/// <summary>
/// Difference between two code blocks
/// </summary>
public class CodeDifference
{
    /// <summary>
    /// Type of difference
    /// </summary>
    public required DifferenceType Type { get; init; }

    /// <summary>
    /// Description of the difference
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Location in first code block
    /// </summary>
    public required CodeLocation Location1 { get; init; }

    /// <summary>
    /// Location in second code block
    /// </summary>
    public required CodeLocation Location2 { get; init; }

    /// <summary>
    /// Text from first code block
    /// </summary>
    public required string Text1 { get; init; }

    /// <summary>
    /// Text from second code block
    /// </summary>
    public required string Text2 { get; init; }

    /// <summary>
    /// Impact of this difference (0.0-1.0)
    /// </summary>
    public required double Impact { get; init; }
}

/// <summary>
/// Location in source code
/// </summary>
public class CodeLocation
{
    /// <summary>
    /// Line number (1-based)
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column number (1-based)
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// Length of the location
    /// </summary>
    public required int Length { get; init; }
}

/// <summary>
/// Common pattern found in duplicate code
/// </summary>
public class CommonPattern
{
    /// <summary>
    /// Pattern type
    /// </summary>
    public required PatternType Type { get; init; }

    /// <summary>
    /// Pattern description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Frequency of this pattern
    /// </summary>
    public required int Frequency { get; init; }

    /// <summary>
    /// Confidence score for this pattern
    /// </summary>
    public required double Confidence { get; init; }
}
