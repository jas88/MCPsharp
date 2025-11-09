namespace MCPsharp.Models.DuplicateDetection;

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
}
