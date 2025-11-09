using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;

namespace MCPsharp.Services.Caching;

/// <summary>
/// Cache for semantic models to avoid repeated compilation
/// </summary>
public sealed class SemanticModelCache : IDisposable
{
    private readonly ConcurrentDictionary<DocumentId, WeakReference<SemanticModel>> _cache = new();
    private readonly ConcurrentDictionary<DocumentId, int> _documentVersions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _disposeLock = new();
    private bool _disposed = false;

    /// <summary>
    /// Get or create a semantic model for a document
    /// </summary>
    /// <param name="document">The document to get semantic model for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Semantic model or null if creation failed</returns>
    public async Task<SemanticModel?> GetOrCreateAsync(
        Document document,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_cache.TryGetValue(document.Id, out var weakRef) &&
            weakRef.TryGetTarget(out var cached) &&
            await IsDocumentVersionCurrent(document))
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(document.Id, out weakRef) &&
                weakRef.TryGetTarget(out cached) &&
                await IsDocumentVersionCurrent(document))
            {
                return cached;
            }

            var model = await document.GetSemanticModelAsync(ct);
            if (model != null)
            {
                _cache[document.Id] = new WeakReference<SemanticModel>(model);
                _documentVersions[document.Id] = await GetDocumentVersion(document);
            }
            return model;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Invalidate cache for a specific document
    /// </summary>
    /// <param name="docId">Document ID to invalidate</param>
    public void Invalidate(DocumentId docId)
    {
        ThrowIfDisposed();
        _cache.TryRemove(docId, out _);
        _documentVersions.TryRemove(docId, out _);
    }

    /// <summary>
    /// Invalidate all cached semantic models
    /// </summary>
    public void InvalidateAll()
    {
        ThrowIfDisposed();
        _cache.Clear();
        _documentVersions.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    /// <returns>Cache statistics</returns>
    public CacheStatistics GetStatistics()
    {
        ThrowIfDisposed();
        
        var totalEntries = _cache.Count;
        var liveEntries = 0;
        var deadEntries = 0;

        foreach (var kvp in _cache)
        {
            if (kvp.Value.TryGetTarget(out _))
            {
                liveEntries++;
            }
            else
            {
                deadEntries++;
            }
        }

        return new CacheStatistics
        {
            TotalEntries = totalEntries,
            LiveEntries = liveEntries,
            DeadEntries = deadEntries,
            HitRate = CalculateHitRate()
        };
    }

    /// <summary>
    /// Clean up dead weak references
    /// </summary>
    /// <returns>Number of entries cleaned up</returns>
    public int Cleanup()
    {
        ThrowIfDisposed();
        
        var toRemove = new List<DocumentId>();
        
        foreach (var kvp in _cache)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var docId in toRemove)
        {
            _cache.TryRemove(docId, out _);
            _documentVersions.TryRemove(docId, out _);
        }

        return toRemove.Count;
    }

    private async Task<bool> IsDocumentVersionCurrent(Document document)
    {
        if (!_documentVersions.TryGetValue(document.Id, out var cachedVersion))
        {
            return false;
        }

        var currentVersion = await GetDocumentVersion(document);
        return cachedVersion == currentVersion;
    }

    private static async Task<int> GetDocumentVersion(Document document)
    {
        // Use document's version stamp if available
        var version = await document.GetSyntaxVersionAsync();
        return version.GetHashCode();
    }

    private double CalculateHitRate()
    {
        // This is a simplified implementation
        // In a real scenario, we'd track hits and misses over time
        var totalEntries = _cache.Count;
        if (totalEntries == 0) return 0.0;

        var liveEntries = _cache.Values.Count(wr => wr.TryGetTarget(out _));
        return (double)liveEntries / totalEntries;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SemanticModelCache));
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                _cache.Clear();
                _documentVersions.Clear();
                _lock.Dispose();
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Cache statistics for monitoring
/// </summary>
public sealed class CacheStatistics
{
    public int TotalEntries { get; init; }
    public int LiveEntries { get; init; }
    public int DeadEntries { get; init; }
    public double HitRate { get; init; }

    public override string ToString()
    {
        return $"Entries: {LiveEntries}/{TotalEntries} (Dead: {DeadEntries}), Hit Rate: {HitRate:P1}";
    }
}