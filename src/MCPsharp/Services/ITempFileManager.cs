using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MCPsharp.Services;

/// <summary>
/// Interface for managing temporary files during streaming operations
/// </summary>
public interface ITempFileManager
{
    /// <summary>
    /// Create a new temporary file
    /// </summary>
    Task<string> CreateTempFileAsync(string? prefix = null, string? extension = null, string? operationId = null);

    /// <summary>
    /// Create a new temporary file (synchronous version)
    /// </summary>
    string CreateTempFile(string? prefix = null, string? extension = null, string? operationId = null);

    /// <summary>
    /// Create a temporary directory
    /// </summary>
    Task<string> CreateTempDirectoryAsync(string? prefix = null, string? operationId = null);

    /// <summary>
    /// Get a temporary file path without creating the file
    /// </summary>
    string GetTempFilePath(string? prefix = null, string? extension = null, string? operationId = null);

    /// <summary>
    /// Register an existing file as temporary for cleanup
    /// </summary>
    Task RegisterTempFileAsync(string filePath, string? operationId = null);

    /// <summary>
    /// Check if a file is a temporary file
    /// </summary>
    Task<bool> IsTempFileAsync(string filePath);

    /// <summary>
    /// Get all temporary files for an operation
    /// </summary>
    Task<List<string>> GetTempFilesAsync(string operationId);

    /// <summary>
    /// Delete a specific temporary file
    /// </summary>
    Task<bool> DeleteTempFileAsync(string filePath);

    /// <summary>
    /// Clean up temporary files for an operation
    /// </summary>
    Task CleanupAsync(string operationId);

    /// <summary>
    /// Clean up old temporary files
    /// </summary>
    Task CleanupAsync(TimeSpan olderThan);

    /// <summary>
    /// Get total size of temporary files
    /// </summary>
    Task<long> GetTempFileSizeAsync();

    /// <summary>
    /// Get temporary file statistics
    /// </summary>
    Task<TempFileStats> GetStatsAsync();
}

/// <summary>
/// Statistics for temporary files
/// </summary>
public class TempFileStats
{
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public int DirectoryCount { get; set; }
    public Dictionary<string, int> FilesByOperation { get; set; } = new();
    public Dictionary<string, long> SizeByExtension { get; set; } = new();
    public DateTime OldestFile { get; set; }
    public DateTime NewestFile { get; set; }
}