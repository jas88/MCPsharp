using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// Service for managing temporary files during streaming operations
/// </summary>
public class TempFileManagerService : ITempFileManager, IDisposable
{
    private readonly ILogger<TempFileManagerService> _logger;
    private readonly string _tempBasePath;
    private readonly ConcurrentDictionary<string, HashSet<string>> _operationFiles;
    private readonly ConcurrentDictionary<string, DateTime> _fileCreationTimes;
    private readonly Timer _cleanupTimer;
    private readonly object _lock = new object();

    public TempFileManagerService(ILogger<TempFileManagerService>? logger = null)
    {
        _logger = logger ?? NullLogger<TempFileManagerService>.Instance;
        _tempBasePath = Path.Combine(Path.GetTempPath(), "MCPsharp", "Streaming");
        _operationFiles = new ConcurrentDictionary<string, HashSet<string>>();
        _fileCreationTimes = new ConcurrentDictionary<string, DateTime>();

        // Ensure temp directory exists
        Directory.CreateDirectory(_tempBasePath);

        // Run cleanup every 10 minutes
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _logger.LogInformation("Initialized temporary file manager with base path: {TempBasePath}", _tempBasePath);
    }

    public async Task<string> CreateTempFileAsync(string? prefix = null, string? extension = null, string? operationId = null)
    {
        prefix ??= "mcp";
        extension ??= ".tmp";

        var fileName = $"{prefix}_{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_tempBasePath, fileName);

        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // Create the file
        using var fs = File.Create(filePath);

        await RegisterTempFileAsync(filePath, operationId);

        _logger.LogDebug("Created temporary file: {FilePath}", filePath);
        return filePath;
    }

    public string CreateTempFile(string? prefix = null, string? extension = null, string? operationId = null)
    {
        prefix ??= "mcp";
        extension ??= ".tmp";

        var fileName = $"{prefix}_{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_tempBasePath, fileName);

        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // Create the file
        using var fs = File.Create(filePath);

        // Register the file synchronously
        if (!string.IsNullOrEmpty(operationId))
        {
            _operationFiles.AddOrUpdate(operationId,
                new HashSet<string> { filePath },
                (key, existing) => { existing.Add(filePath); return existing; });
        }

        _fileCreationTimes[filePath] = DateTime.UtcNow;

        _logger.LogDebug("Created temporary file: {FilePath}", filePath);
        return filePath;
    }

    public async Task<string> CreateTempDirectoryAsync(string? prefix = null, string? operationId = null)
    {
        prefix ??= "mcp";

        var dirName = $"{prefix}_{Guid.NewGuid()}";
        var dirPath = Path.Combine(_tempBasePath, dirName);

        Directory.CreateDirectory(dirPath);

        if (!string.IsNullOrEmpty(operationId))
        {
            _operationFiles.AddOrUpdate(operationId,
                new HashSet<string> { dirPath },
                (key, existing) => { existing.Add(dirPath); return existing; });
        }

        _fileCreationTimes[dirPath] = DateTime.UtcNow;

        _logger.LogDebug("Created temporary directory: {DirPath}", dirPath);
        return dirPath;
    }

    public string GetTempFilePath(string? prefix = null, string? extension = null, string? operationId = null)
    {
        prefix ??= "mcp";
        extension ??= ".tmp";

        var fileName = $"{prefix}_{Guid.NewGuid()}{extension}";
        return Path.Combine(_tempBasePath, fileName);
    }

    public async Task RegisterTempFileAsync(string filePath, string? operationId = null)
    {
        if (!string.IsNullOrEmpty(operationId))
        {
            _operationFiles.AddOrUpdate(operationId,
                new HashSet<string> { filePath },
                (key, existing) => { existing.Add(filePath); return existing; });
        }

        _fileCreationTimes[filePath] = DateTime.UtcNow;

        _logger.LogDebug("Registered temporary file: {FilePath} for operation: {OperationId}", filePath, operationId);
    }

    public async Task<bool> IsTempFileAsync(string filePath)
    {
        return filePath.StartsWith(_tempBasePath, StringComparison.OrdinalIgnoreCase) &&
               _fileCreationTimes.ContainsKey(filePath);
    }

    public async Task<List<string>> GetTempFilesAsync(string operationId)
    {
        if (_operationFiles.TryGetValue(operationId, out var files))
        {
            return files.ToList();
        }
        return new List<string>();
    }

    public async Task<bool> DeleteTempFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted temporary file: {FilePath}", filePath);
            }
            else if (Directory.Exists(filePath))
            {
                Directory.Delete(filePath, recursive: true);
                _logger.LogDebug("Deleted temporary directory: {FilePath}", filePath);
            }

            // Remove from tracking
            _fileCreationTimes.TryRemove(filePath, out _);

            // Remove from operation tracking
            foreach (var kvp in _operationFiles)
            {
                kvp.Value.Remove(filePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting temporary file: {FilePath}", filePath);
            return false;
        }
    }

    public async Task CleanupAsync(string operationId)
    {
        if (_operationFiles.TryRemove(operationId, out var files))
        {
            var tasks = files.Select(DeleteTempFileAsync);
            await Task.WhenAll(tasks);

            _logger.LogInformation("Cleaned up {Count} temporary files for operation: {OperationId}",
                files.Count, operationId);
        }
    }

    public async Task CleanupAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTime.UtcNow - olderThan;
        var filesToDelete = _fileCreationTimes
            .Where(kvp => kvp.Value < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        var deleteTasks = filesToDelete.Select(DeleteTempFileAsync);
        var results = await Task.WhenAll(deleteTasks);

        var deletedCount = results.Count(r => r);
        if (deletedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} temporary files older than {OlderThan}",
                deletedCount, olderThan);
        }
    }

    public async Task<long> GetTempFileSizeAsync()
    {
        long totalSize = 0;

        foreach (var filePath in _fileCreationTimes.Keys)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    totalSize += fileInfo.Length;
                }
                else if (Directory.Exists(filePath))
                {
                    totalSize += GetDirectorySize(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting size for temp file: {FilePath}", filePath);
            }
        }

        return totalSize;
    }

    public async Task<TempFileStats> GetStatsAsync()
    {
        var stats = new TempFileStats();
        var filesByExtension = new Dictionary<string, long>();
        var filesByOperation = new Dictionary<string, int>();

        DateTime oldestFile = DateTime.UtcNow;
        DateTime newestFile = DateTime.MinValue;

        foreach (var kvp in _fileCreationTimes)
        {
            var filePath = kvp.Key;
            var creationTime = kvp.Value;

            oldestFile = creationTime < oldestFile ? creationTime : oldestFile;
            newestFile = creationTime > newestFile ? creationTime : newestFile;

            try
            {
                if (File.Exists(filePath))
                {
                    stats.FileCount++;
                    var fileInfo = new FileInfo(filePath);

                    var extension = fileInfo.Extension.ToLowerInvariant();
                    filesByExtension[extension] = filesByExtension.GetValueOrDefault(extension) + fileInfo.Length;
                }
                else if (Directory.Exists(filePath))
                {
                    stats.DirectoryCount++;
                    filesByExtension[".dir"] = filesByExtension.GetValueOrDefault(".dir") + GetDirectorySize(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting stats for temp file: {FilePath}", filePath);
            }
        }

        // Count files by operation
        foreach (var kvp in _operationFiles)
        {
            filesByOperation[kvp.Key] = kvp.Value.Count;
        }

        stats.FilesByOperation = filesByOperation;
        stats.SizeByExtension = filesByExtension;
        stats.TotalSizeBytes = await GetTempFileSizeAsync();
        stats.OldestFile = oldestFile;
        stats.NewestFile = newestFile;

        return stats;
    }

    private long GetDirectorySize(string directoryPath)
    {
        long size = 0;
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating directory size: {DirectoryPath}", directoryPath);
        }
        return size;
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            CleanupAsync(TimeSpan.FromHours(2)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic temporary file cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();

        // Clean up all temp files on disposal
        try
        {
            CleanupAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal cleanup");
        }
    }
}