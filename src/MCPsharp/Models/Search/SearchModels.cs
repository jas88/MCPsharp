namespace MCPsharp.Models.Search;

/// <summary>
/// Request parameters for text search
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// Text or regex pattern to search for
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// File or directory to search (default: current project)
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// Treat pattern as regex (default: false)
    /// </summary>
    public bool Regex { get; init; } = false;

    /// <summary>
    /// Case-sensitive search (default: true)
    /// </summary>
    public bool CaseSensitive { get; init; } = true;

    /// <summary>
    /// Glob pattern for files to include (e.g., "*.cs")
    /// </summary>
    public string? IncludePattern { get; init; }

    /// <summary>
    /// Glob pattern for files to exclude
    /// </summary>
    public string? ExcludePattern { get; init; }

    /// <summary>
    /// Lines of context before/after match (default: 2)
    /// </summary>
    public int ContextLines { get; init; } = 2;

    /// <summary>
    /// Maximum results to return (default: 100)
    /// </summary>
    public int MaxResults { get; init; } = 100;

    /// <summary>
    /// Pagination offset (default: 0)
    /// </summary>
    public int Offset { get; init; } = 0;
}

/// <summary>
/// Search result with matches and pagination info
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Whether the search succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Total number of matches found
    /// </summary>
    public required int TotalMatches { get; init; }

    /// <summary>
    /// Number of matches returned in this response
    /// </summary>
    public required int Returned { get; init; }

    /// <summary>
    /// Offset used for pagination
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Whether there are more results available
    /// </summary>
    public required bool HasMore { get; init; }

    /// <summary>
    /// List of search matches
    /// </summary>
    public required List<SearchMatch> Matches { get; init; }

    /// <summary>
    /// Number of files searched
    /// </summary>
    public required int FilesSearched { get; init; }

    /// <summary>
    /// Search duration in milliseconds
    /// </summary>
    public required long SearchDurationMs { get; init; }

    /// <summary>
    /// Error message if search failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Individual search match
/// </summary>
public class SearchMatch
{
    /// <summary>
    /// Absolute path to the file containing the match
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Line number of the match (1-based)
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Column number of the match (1-based)
    /// </summary>
    public required int ColumnNumber { get; init; }

    /// <summary>
    /// The matched text
    /// </summary>
    public required string MatchText { get; init; }

    /// <summary>
    /// Lines of context before the match
    /// </summary>
    public required List<string> ContextBefore { get; init; }

    /// <summary>
    /// Lines of context after the match
    /// </summary>
    public required List<string> ContextAfter { get; init; }

    /// <summary>
    /// Full line content containing the match
    /// </summary>
    public required string LineContent { get; init; }
}
