using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Navigation;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Multi-level cache for navigation operations to improve performance
/// </summary>
public class NavigationCache : INavigationCache
{
    private readonly IMemoryCache _symbolCache;
    private readonly IMemoryCache _hierarchyCache;
    private readonly IMemoryCache _overloadCache;
    private readonly ConcurrentDictionary<string, HashSet<string>> _fileToSymbolKeys;
    private readonly ILogger<NavigationCache>? _logger;

    public NavigationCache(ILogger<NavigationCache>? logger = null)
    {
        _logger = logger;

        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = 10000,
            CompactionPercentage = 0.25
        };

        _symbolCache = new MemoryCache(cacheOptions);
        _hierarchyCache = new MemoryCache(cacheOptions);
        _overloadCache = new MemoryCache(cacheOptions);
        _fileToSymbolKeys = new ConcurrentDictionary<string, HashSet<string>>();
    }

    /// <summary>
    /// Cache type hierarchy information
    /// </summary>
    public void CacheSymbolHierarchy(INamedTypeSymbol type, HierarchyInfo info)
    {
        var key = GetHierarchyKey(type);

        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(15))
            .RegisterPostEvictionCallback(OnHierarchyCacheEviction);

        _hierarchyCache.Set(key, info, entryOptions);

        // Track which files this hierarchy is associated with
        foreach (var location in type.Locations.Where(l => l.IsInSource))
        {
            var filePath = location.GetLineSpan().Path;
            TrackSymbolInFile(filePath, key);
        }

        _logger?.LogDebug("Cached hierarchy for type {TypeName} with key {Key}",
            type.Name, key);
    }

    /// <summary>
    /// Get cached hierarchy information
    /// </summary>
    public HierarchyInfo? GetCachedHierarchy(INamedTypeSymbol type)
    {
        var key = GetHierarchyKey(type);

        if (_hierarchyCache.TryGetValue(key, out HierarchyInfo? cachedInfo))
        {
            _logger?.LogDebug("Cache hit for hierarchy {TypeName}", type.Name);
            return cachedInfo;
        }

        _logger?.LogDebug("Cache miss for hierarchy {TypeName}", type.Name);
        return null;
    }

    /// <summary>
    /// Invalidate cache entries for a specific file
    /// </summary>
    public void InvalidateForFile(string filePath)
    {
        _logger?.LogInformation("Invalidating cache for file {FilePath}", filePath);

        if (_fileToSymbolKeys.TryRemove(filePath, out var symbolKeys))
        {
            foreach (var key in symbolKeys)
            {
                _symbolCache.Remove(key);
                _hierarchyCache.Remove(key);
                _overloadCache.Remove(key);
            }

            _logger?.LogDebug("Invalidated {Count} cache entries for file {FilePath}",
                symbolKeys.Count, filePath);
        }
    }

    /// <summary>
    /// Clear all cached data
    /// </summary>
    public void Clear()
    {
        _logger?.LogInformation("Clearing all navigation caches");

        // Dispose and recreate caches
        (_symbolCache as MemoryCache)?.Compact(1.0);
        (_hierarchyCache as MemoryCache)?.Compact(1.0);
        (_overloadCache as MemoryCache)?.Compact(1.0);

        _fileToSymbolKeys.Clear();
    }

    /// <summary>
    /// Cache symbol resolution result
    /// </summary>
    public void CacheResolvedSymbol(string filePath, int line, int column, ResolvedSymbol symbol)
    {
        var key = GetPositionKey(filePath, line, column);

        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(5))
            .RegisterPostEvictionCallback(OnSymbolCacheEviction);

        _symbolCache.Set(key, symbol, entryOptions);
        TrackSymbolInFile(filePath, key);

        _logger?.LogDebug("Cached resolved symbol at {FilePath}:{Line}:{Column}",
            filePath, line, column);
    }

    /// <summary>
    /// Get cached resolved symbol
    /// </summary>
    public ResolvedSymbol? GetCachedResolvedSymbol(string filePath, int line, int column)
    {
        var key = GetPositionKey(filePath, line, column);

        if (_symbolCache.TryGetValue(key, out ResolvedSymbol? symbol))
        {
            _logger?.LogDebug("Cache hit for symbol at {FilePath}:{Line}:{Column}",
                filePath, line, column);
            return symbol;
        }

        return null;
    }

    /// <summary>
    /// Cache method overloads
    /// </summary>
    public void CacheOverloads(IMethodSymbol method, OverloadInfo overloads)
    {
        var key = GetOverloadKey(method);

        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(10))
            .RegisterPostEvictionCallback(OnOverloadCacheEviction);

        _overloadCache.Set(key, overloads, entryOptions);

        // Track in file mapping
        foreach (var location in method.Locations.Where(l => l.IsInSource))
        {
            var filePath = location.GetLineSpan().Path;
            TrackSymbolInFile(filePath, key);
        }

        _logger?.LogDebug("Cached {Count} overloads for method {MethodName}",
            overloads.Overloads.Count, method.Name);
    }

    /// <summary>
    /// Get cached overloads
    /// </summary>
    public OverloadInfo? GetCachedOverloads(IMethodSymbol method)
    {
        var key = GetOverloadKey(method);

        if (_overloadCache.TryGetValue(key, out OverloadInfo? overloads))
        {
            _logger?.LogDebug("Cache hit for overloads of {MethodName}", method.Name);
            return overloads;
        }

        return null;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            SymbolCacheCount = (_symbolCache as MemoryCache)?.Count ?? 0,
            HierarchyCacheCount = (_hierarchyCache as MemoryCache)?.Count ?? 0,
            OverloadCacheCount = (_overloadCache as MemoryCache)?.Count ?? 0,
            TrackedFiles = _fileToSymbolKeys.Count,
            TotalTrackedSymbols = _fileToSymbolKeys.Values.Sum(v => v.Count)
        };
    }

    // Private helper methods

    private void TrackSymbolInFile(string filePath, string symbolKey)
    {
        _fileToSymbolKeys.AddOrUpdate(
            filePath,
            new HashSet<string> { symbolKey },
            (_, existing) =>
            {
                existing.Add(symbolKey);
                return existing;
            });
    }

    private string GetHierarchyKey(INamedTypeSymbol type)
    {
        // Create a unique key for the type hierarchy
        return $"hierarchy:{type.ContainingAssembly?.Name}:{type.ToDisplayString()}";
    }

    private string GetPositionKey(string filePath, int line, int column)
    {
        return $"position:{filePath}:{line}:{column}";
    }

    private string GetOverloadKey(IMethodSymbol method)
    {
        return $"overload:{method.ContainingType?.ToDisplayString()}:{method.Name}";
    }

    private void OnSymbolCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        _logger?.LogDebug("Symbol cache entry evicted: {Key}, Reason: {Reason}", key, reason);
    }

    private void OnHierarchyCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        _logger?.LogDebug("Hierarchy cache entry evicted: {Key}, Reason: {Reason}", key, reason);
    }

    private void OnOverloadCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        _logger?.LogDebug("Overload cache entry evicted: {Key}, Reason: {Reason}", key, reason);
    }

    /// <summary>
    /// Dispose of cache resources
    /// </summary>
    public void Dispose()
    {
        (_symbolCache as IDisposable)?.Dispose();
        (_hierarchyCache as IDisposable)?.Dispose();
        (_overloadCache as IDisposable)?.Dispose();
    }
}

/// <summary>
/// Interface for navigation cache
/// </summary>
public interface INavigationCache
{
    void CacheSymbolHierarchy(INamedTypeSymbol type, HierarchyInfo info);
    HierarchyInfo? GetCachedHierarchy(INamedTypeSymbol type);
    void InvalidateForFile(string filePath);
    void Clear();
    void CacheResolvedSymbol(string filePath, int line, int column, ResolvedSymbol symbol);
    ResolvedSymbol? GetCachedResolvedSymbol(string filePath, int line, int column);
    void CacheOverloads(IMethodSymbol method, OverloadInfo overloads);
    OverloadInfo? GetCachedOverloads(IMethodSymbol method);
    CacheStatistics GetStatistics();
}

/// <summary>
/// Cache statistics for monitoring
/// </summary>
public class CacheStatistics
{
    public int SymbolCacheCount { get; init; }
    public int HierarchyCacheCount { get; init; }
    public int OverloadCacheCount { get; init; }
    public int TrackedFiles { get; init; }
    public int TotalTrackedSymbols { get; init; }
}