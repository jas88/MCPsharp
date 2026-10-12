using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Navigation;

namespace MCPsharp.Services.Navigation;

/// <summary>
/// Multi-level cache for navigation operations to improve performance
/// </summary>
public partial class NavigationCache : INavigationCache, IDisposable
{
    private readonly MemoryCache _symbolCache;
    private readonly MemoryCache _hierarchyCache;
    private readonly MemoryCache _overloadCache;
    private readonly ConcurrentDictionary<string, HashSet<string>> _fileToSymbolKeys;
    private readonly ILogger<NavigationCache> _logger;
    private bool _disposed;

    public NavigationCache(ILogger<NavigationCache>? logger = null)
    {
        _logger = logger ?? NullLogger<NavigationCache>.Instance;

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

        LogCachedHierarchy(_logger, type.Name, key);
    }

    /// <summary>
    /// Get cached hierarchy information
    /// </summary>
    public HierarchyInfo? GetCachedHierarchy(INamedTypeSymbol type)
    {
        var key = GetHierarchyKey(type);

        if (_hierarchyCache.TryGetValue(key, out HierarchyInfo? cachedInfo))
        {
            LogHierarchyCacheHit(_logger, type.Name);
            return cachedInfo;
        }

        LogHierarchyCacheMiss(_logger, type.Name);
        return null;
    }

    /// <summary>
    /// Invalidate cache entries for a specific file
    /// </summary>
    public void InvalidateForFile(string filePath)
    {
        LogInvalidatingFile(_logger, filePath);

        if (_fileToSymbolKeys.TryRemove(filePath, out var symbolKeys))
        {
            foreach (var key in symbolKeys)
            {
                _symbolCache.Remove(key);
                _hierarchyCache.Remove(key);
                _overloadCache.Remove(key);
            }

            LogInvalidatedEntries(_logger, symbolKeys.Count, filePath);
        }
    }

    /// <summary>
    /// Clear all cached data
    /// </summary>
    public void Clear()
    {
        LogClearingCaches(_logger);

        // Evict every entry from each cache
        _symbolCache.Compact(1.0);
        _hierarchyCache.Compact(1.0);
        _overloadCache.Compact(1.0);

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

        LogCachedResolvedSymbol(_logger, filePath, line, column);
    }

    /// <summary>
    /// Get cached resolved symbol
    /// </summary>
    public ResolvedSymbol? GetCachedResolvedSymbol(string filePath, int line, int column)
    {
        var key = GetPositionKey(filePath, line, column);

        if (_symbolCache.TryGetValue(key, out ResolvedSymbol? symbol))
        {
            LogSymbolCacheHit(_logger, filePath, line, column);
            return symbol;
        }

        return null;
    }

    /// <summary>
    /// Cache method overloads
    /// </summary>
    public void CacheOverloads(IMethodSymbol method, OverloadInfo overloadInfo)
    {
        var key = GetOverloadKey(method);

        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(10))
            .RegisterPostEvictionCallback(OnOverloadCacheEviction);

        _overloadCache.Set(key, overloadInfo, entryOptions);

        // Track in file mapping
        foreach (var location in method.Locations.Where(l => l.IsInSource))
        {
            var filePath = location.GetLineSpan().Path;
            TrackSymbolInFile(filePath, key);
        }

        LogCachedOverloads(_logger, overloadInfo.Overloads.Count, method.Name);
    }

    /// <summary>
    /// Get cached overloads
    /// </summary>
    public OverloadInfo? GetCachedOverloads(IMethodSymbol method)
    {
        var key = GetOverloadKey(method);

        if (_overloadCache.TryGetValue(key, out OverloadInfo? overloads))
        {
            LogOverloadCacheHit(_logger, method.Name);
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
            SymbolCacheCount = _symbolCache.Count,
            HierarchyCacheCount = _hierarchyCache.Count,
            OverloadCacheCount = _overloadCache.Count,
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

    private static string GetHierarchyKey(INamedTypeSymbol type)
    {
        // Create a unique key for the type hierarchy
        return $"hierarchy:{type.ContainingAssembly?.Name}:{type.ToDisplayString()}";
    }

    private static string GetPositionKey(string filePath, int line, int column)
    {
        return $"position:{filePath}:{line}:{column}";
    }

    private static string GetOverloadKey(IMethodSymbol method)
    {
        return $"overload:{method.ContainingType?.ToDisplayString()}:{method.Name}";
    }

    private void OnSymbolCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        LogSymbolEvicted(_logger, key, reason);
    }

    private void OnHierarchyCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        LogHierarchyEvicted(_logger, key, reason);
    }

    private void OnOverloadCacheEviction(object key, object? value, EvictionReason reason, object? state)
    {
        LogOverloadEvicted(_logger, key, reason);
    }

    /// <summary>
    /// Dispose of cache resources
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the underlying memory caches when <paramref name="disposing"/> is true.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _symbolCache.Dispose();
            _hierarchyCache.Dispose();
            _overloadCache.Dispose();
        }

        _disposed = true;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached hierarchy for type {TypeName} with key {Key}")]
    private static partial void LogCachedHierarchy(ILogger logger, string typeName, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for hierarchy {TypeName}")]
    private static partial void LogHierarchyCacheHit(ILogger logger, string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for hierarchy {TypeName}")]
    private static partial void LogHierarchyCacheMiss(ILogger logger, string typeName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invalidating cache for file {FilePath}")]
    private static partial void LogInvalidatingFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalidated {Count} cache entries for file {FilePath}")]
    private static partial void LogInvalidatedEntries(ILogger logger, int count, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Clearing all navigation caches")]
    private static partial void LogClearingCaches(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached resolved symbol at {FilePath}:{Line}:{Column}")]
    private static partial void LogCachedResolvedSymbol(ILogger logger, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for symbol at {FilePath}:{Line}:{Column}")]
    private static partial void LogSymbolCacheHit(ILogger logger, string filePath, int line, int column);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached {Count} overloads for method {MethodName}")]
    private static partial void LogCachedOverloads(ILogger logger, int count, string methodName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for overloads of {MethodName}")]
    private static partial void LogOverloadCacheHit(ILogger logger, string methodName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Symbol cache entry evicted: {Key}, Reason: {Reason}")]
    private static partial void LogSymbolEvicted(ILogger logger, object key, EvictionReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hierarchy cache entry evicted: {Key}, Reason: {Reason}")]
    private static partial void LogHierarchyEvicted(ILogger logger, object key, EvictionReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overload cache entry evicted: {Key}, Reason: {Reason}")]
    private static partial void LogOverloadEvicted(ILogger logger, object key, EvictionReason reason);
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
    void CacheOverloads(IMethodSymbol method, OverloadInfo overloadInfo);
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
