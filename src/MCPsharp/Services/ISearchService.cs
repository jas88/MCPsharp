using MCPsharp.Models.Search;

namespace MCPsharp.Services;

/// <summary>
/// Service for searching text and regex patterns across project files
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Search for text or regex patterns across project files
    /// </summary>
    /// <param name="request">Search request parameters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Search results with matches, context, and pagination info</returns>
    Task<SearchResult> SearchTextAsync(SearchRequest request, CancellationToken ct = default);
}
