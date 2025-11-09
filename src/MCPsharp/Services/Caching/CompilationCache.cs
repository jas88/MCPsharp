using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;

namespace MCPsharp.Services.Caching;

/// <summary>
/// Cache for compilations to avoid expensive recompilation
/// </summary>
public sealed class CompilationCache : IDisposable
{
    private readonly ConcurrentDictionary<ProjectId, WeakReference<Compilation>> _cache = new();
    private readonly ConcurrentDictionary<ProjectId, int> _projectVersions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _disposeLock = new();
    private bool _disposed = false;

    /// <summary>
    /// Get or create a compilation for a project
    /// </summary>
    /// <param name="project">The project to get compilation for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Compilation or null if creation failed</returns>
    public async Task<Compilation?> GetOrCreateAsync(
        Project project,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_cache.TryGetValue(project.Id, out var weakRef) &&
            weakRef.TryGetTarget(out var cached) &&
            IsProjectVersionCurrent(project))
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(project.Id, out weakRef) &&
                weakRef.TryGetTarget(out cached) &&
                IsProjectVersionCurrent(project))
            {
                return cached;
            }

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation != null)
            {
                _cache[project.Id] = new WeakReference<Compilation>(compilation);
                _projectVersions[project.Id] = GetProjectVersion(project);
            }
            return compilation;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Invalidate cache for a specific project
    /// </summary>
    /// <param name="projectId">Project ID to invalidate</param>
    public void Invalidate(ProjectId projectId)
    {
        ThrowIfDisposed();
        _cache.TryRemove(projectId, out _);
        _projectVersions.TryRemove(projectId, out _);
    }

    /// <summary>
    /// Invalidate all cached compilations
    /// </summary>
    public void InvalidateAll()
    {
        ThrowIfDisposed();
        _cache.Clear();
        _projectVersions.Clear();
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
        
        var toRemove = new List<ProjectId>();
        
        foreach (var kvp in _cache)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var projectId in toRemove)
        {
            _cache.TryRemove(projectId, out _);
            _projectVersions.TryRemove(projectId, out _);
        }

        return toRemove.Count;
    }

    private bool IsProjectVersionCurrent(Project project)
    {
        if (!_projectVersions.TryGetValue(project.Id, out var cachedVersion))
        {
            return false;
        }

        var currentVersion = GetProjectVersion(project);
        return cachedVersion == currentVersion;
    }

    private static int GetProjectVersion(Project project)
    {
        // Use project's version stamp if available
        var version = project.Version;
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
            throw new ObjectDisposedException(nameof(CompilationCache));
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                _cache.Clear();
                _projectVersions.Clear();
                _lock.Dispose();
                _disposed = true;
            }
        }
    }
}