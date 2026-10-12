namespace MCPsharp.Services;

/// <summary>
/// Mock implementation of ITempFileManager for testing
/// </summary>
internal sealed class MockTempFileManager : ITempFileManager
{
    public Task<string> CreateTempFileAsync(string? prefix = null, string? extension = null, string? operationId = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid()}{extension}");
        File.WriteAllText(tempFile, "");
        return Task.FromResult(tempFile);
    }

    public string CreateTempFile(string? prefix = null, string? extension = null, string? operationId = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid()}{extension}");
        File.WriteAllText(tempFile, "");
        return tempFile;
    }

    public Task<string> CreateTempDirectoryAsync(string? prefix = null, string? operationId = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return Task.FromResult(tempDir);
    }

    public string GetTempFilePath(string? prefix = null, string? extension = null, string? operationId = null)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid()}{extension}");
    }

    public Task RegisterTempFileAsync(string filePath, string? operationId = null)
    {
        return Task.CompletedTask;
    }

    public Task<bool> IsTempFileAsync(string filePath)
    {
        return Task.FromResult(false);
    }

    public Task<List<string>> GetTempFilesAsync(string operationId)
    {
        return Task.FromResult(new List<string>());
    }

    public Task<bool> DeleteTempFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    public Task CleanupAsync(string operationId)
    {
        return Task.CompletedTask;
    }

    public Task CleanupAsync(TimeSpan olderThan)
    {
        return Task.CompletedTask;
    }

    public Task<long> GetTempFileSizeAsync()
    {
        return Task.FromResult(0L);
    }

    public Task<TempFileStats> GetStatsAsync()
    {
        return Task.FromResult(new TempFileStats());
    }
}
