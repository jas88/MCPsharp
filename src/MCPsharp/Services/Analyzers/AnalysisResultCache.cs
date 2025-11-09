using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for caching analysis results to avoid re-analyzing unchanged files
/// </summary>
public interface IAnalysisResultCache
{
    Task<AnalysisResult?> GetCachedResultAsync(string analyzerId, string filePath, string contentHash, CancellationToken cancellationToken = default);
    Task<bool> CacheResultAsync(string analyzerId, string filePath, string contentHash, AnalysisResult result, CancellationToken cancellationToken = default);
    Task<bool> InvalidateCacheAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> InvalidateAnalyzerCacheAsync(string analyzerId, CancellationToken cancellationToken = default);
    Task<bool> ClearCacheAsync(CancellationToken cancellationToken = default);
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    string ComputeContentHash(string content);
}

public class AnalysisResultCache : IAnalysisResultCache
{
    private readonly ILogger<AnalysisResultCache> _logger;
    private readonly Dictionary<string, CachedAnalysisResult> _memoryCache;
    private readonly object _lock = new();
    private readonly string _cacheDirectory;
    private readonly bool _persistToDisk;
    private readonly int _maxMemoryCacheSize;
    private int _cacheHits;
    private int _cacheMisses;

    public AnalysisResultCache(
        ILogger<AnalysisResultCache> logger,
        bool persistToDisk = true,
        int maxMemoryCacheSize = 1000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryCache = new Dictionary<string, CachedAnalysisResult>();
        _persistToDisk = persistToDisk;
        _maxMemoryCacheSize = maxMemoryCacheSize;
        _cacheHits = 0;
        _cacheMisses = 0;

        // Set up cache directory
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _cacheDirectory = Path.Combine(homeDirectory, ".mcpsharp", "cache", "analysis");

        if (_persistToDisk && !Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<AnalysisResult?> GetCachedResultAsync(
        string analyzerId,
        string filePath,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GetCacheKey(analyzerId, filePath, contentHash);

            // Check memory cache first
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    if (!cachedResult.IsExpired())
                    {
                        _cacheHits++;
                        _logger.LogDebug("Cache hit for {FilePath} with analyzer {AnalyzerId}",
                            filePath, analyzerId);
                        return cachedResult.Result;
                    }
                    else
                    {
                        // Remove expired entry
                        _memoryCache.Remove(cacheKey);
                    }
                }
            }

            // Check disk cache if enabled
            if (_persistToDisk)
            {
                var diskResult = await LoadFromDiskAsync(cacheKey, cancellationToken);
                if (diskResult != null && !diskResult.IsExpired())
                {
                    // Promote to memory cache
                    lock (_lock)
                    {
                        if (_memoryCache.Count < _maxMemoryCacheSize)
                        {
                            _memoryCache[cacheKey] = diskResult;
                        }
                    }

                    _cacheHits++;
                    _logger.LogDebug("Cache hit (disk) for {FilePath} with analyzer {AnalyzerId}",
                        filePath, analyzerId);
                    return diskResult.Result;
                }
            }

            _cacheMisses++;
            _logger.LogDebug("Cache miss for {FilePath} with analyzer {AnalyzerId}",
                filePath, analyzerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached result for {FilePath}", filePath);
            return null;
        }
    }

    public async Task<bool> CacheResultAsync(
        string analyzerId,
        string filePath,
        string contentHash,
        AnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GetCacheKey(analyzerId, filePath, contentHash);
            var cachedResult = new CachedAnalysisResult
            {
                AnalyzerId = analyzerId,
                FilePath = filePath,
                ContentHash = contentHash,
                Result = result,
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24) // Cache for 24 hours
            };

            // Add to memory cache
            lock (_lock)
            {
                // Enforce max cache size (LRU eviction)
                if (_memoryCache.Count >= _maxMemoryCacheSize)
                {
                    var oldestKey = _memoryCache
                        .OrderBy(kvp => kvp.Value.CachedAt)
                        .First()
                        .Key;
                    _memoryCache.Remove(oldestKey);
                }

                _memoryCache[cacheKey] = cachedResult;
            }

            // Persist to disk if enabled
            if (_persistToDisk)
            {
                await SaveToDiskAsync(cacheKey, cachedResult, cancellationToken);
            }

            _logger.LogDebug("Cached result for {FilePath} with analyzer {AnalyzerId}",
                filePath, analyzerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching result for {FilePath}", filePath);
            return false;
        }
    }

    public Task<bool> InvalidateCacheAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Invalidating cache for file: {FilePath}", filePath);

            // Remove from memory cache
            lock (_lock)
            {
                var keysToRemove = _memoryCache
                    .Where(kvp => kvp.Value.FilePath == filePath)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _memoryCache.Remove(key);
                }
            }

            // Remove from disk cache if enabled
            if (_persistToDisk)
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json")
                    .Where(f => f.Contains(GetFilePathHash(filePath)));

                foreach (var cacheFile in cacheFiles)
                {
                    try
                    {
                        File.Delete(cacheFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error deleting cache file: {CacheFile}", cacheFile);
                    }
                }
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache for {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    public Task<bool> InvalidateAnalyzerCacheAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Invalidating cache for analyzer: {AnalyzerId}", analyzerId);

            // Remove from memory cache
            lock (_lock)
            {
                var keysToRemove = _memoryCache
                    .Where(kvp => kvp.Value.AnalyzerId == analyzerId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _memoryCache.Remove(key);
                }
            }

            // Remove from disk cache if enabled
            if (_persistToDisk)
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, $"{analyzerId}_*.json");

                foreach (var cacheFile in cacheFiles)
                {
                    try
                    {
                        File.Delete(cacheFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error deleting cache file: {CacheFile}", cacheFile);
                    }
                }
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache for analyzer {AnalyzerId}", analyzerId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Clearing entire analysis cache");

            // Clear memory cache
            lock (_lock)
            {
                _memoryCache.Clear();
                _cacheHits = 0;
                _cacheMisses = 0;
            }

            // Clear disk cache if enabled
            if (_persistToDisk && Directory.Exists(_cacheDirectory))
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                foreach (var cacheFile in cacheFiles)
                {
                    try
                    {
                        File.Delete(cacheFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error deleting cache file: {CacheFile}", cacheFile);
                    }
                }
            }

            _logger.LogInformation("Cache cleared successfully");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            return Task.FromResult(false);
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            int memoryCacheCount;
            int diskCacheCount = 0;
            long diskCacheSize = 0;

            lock (_lock)
            {
                memoryCacheCount = _memoryCache.Count;
            }

            if (_persistToDisk && Directory.Exists(_cacheDirectory))
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                diskCacheCount = cacheFiles.Length;
                diskCacheSize = cacheFiles.Sum(f => new FileInfo(f).Length);
            }

            var totalRequests = _cacheHits + _cacheMisses;
            var hitRate = totalRequests > 0 ? (double)_cacheHits / totalRequests : 0;

            return Task.FromResult(new CacheStatistics
            {
                MemoryCacheCount = memoryCacheCount,
                DiskCacheCount = diskCacheCount,
                DiskCacheSizeBytes = diskCacheSize,
                TotalHits = _cacheHits,
                TotalMisses = _cacheMisses,
                HitRate = hitRate,
                TotalRequests = totalRequests
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            return Task.FromResult(new CacheStatistics());
        }
    }

    public string ComputeContentHash(string content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hashBytes);
    }

    private string GetCacheKey(string analyzerId, string filePath, string contentHash)
    {
        var filePathHash = GetFilePathHash(filePath);
        return $"{analyzerId}_{filePathHash}_{contentHash}";
    }

    private static string GetFilePathHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(filePath));
        return Convert.ToHexString(hashBytes)[..16]; // Use first 16 characters
    }

    private async Task<CachedAnalysisResult?> LoadFromDiskAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cachePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            if (!File.Exists(cachePath))
                return null;

            var json = await File.ReadAllTextAsync(cachePath, cancellationToken);
            return JsonSerializer.Deserialize<CachedAnalysisResult>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading cache from disk: {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task SaveToDiskAsync(string cacheKey, CachedAnalysisResult cachedResult, CancellationToken cancellationToken)
    {
        try
        {
            var cachePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            var json = JsonSerializer.Serialize(cachedResult, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            await File.WriteAllTextAsync(cachePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving cache to disk: {CacheKey}", cacheKey);
        }
    }
}

/// <summary>
/// Represents a cached analysis result
/// </summary>
public class CachedAnalysisResult
{
    public required string AnalyzerId { get; init; }
    public required string FilePath { get; init; }
    public required string ContentHash { get; init; }
    public required AnalysisResult Result { get; init; }
    public required DateTime CachedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }

    public bool IsExpired() => DateTime.UtcNow >= ExpiresAt;
}

/// <summary>
/// Statistics about the analysis cache
/// </summary>
public class CacheStatistics
{
    public int MemoryCacheCount { get; init; }
    public int DiskCacheCount { get; init; }
    public long DiskCacheSizeBytes { get; init; }
    public int TotalHits { get; init; }
    public int TotalMisses { get; init; }
    public double HitRate { get; init; }
    public int TotalRequests { get; init; }
}
