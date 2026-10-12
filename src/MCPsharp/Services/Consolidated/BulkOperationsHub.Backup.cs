using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class BulkOperationsHub
{
    private async Task<string?> CreateBulkBackupAsync(BulkOperationRequest request, CancellationToken ct)
    {
        if (_tempFileManager == null) return null;

        var backupId = Guid.NewGuid().ToString();
        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        Directory.CreateDirectory(backupPath);

        try
        {
            // Backups are named by position so files sharing a name in different directories don't collide;
            // the manifest records where each one is restored to.
            var manifest = new List<(string OriginalPath, string BackupPath)>();
            foreach (var file in (request.Files ?? new List<string>()).Where(File.Exists))
            {
                var backupFile = Path.Combine(backupPath, manifest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Task.Run(() => File.Copy(file, backupFile, true), ct);
                manifest.Add((Path.GetFullPath(file), backupFile));
            }

            lock (_operationsLock)
            {
                _backupManifests[backupId] = manifest;
            }

            return backupId;
        }
        catch (Exception ex)
        {
            LogBulkBackupFailed(_logger, ex, backupPath);
            // Cleanup on failure
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
            return null;
        }
    }

    private async Task RestoreBulkBackupAsync(string backupId, CancellationToken ct)
    {
        List<(string OriginalPath, string BackupPath)>? manifest;
        lock (_operationsLock)
        {
            _backupManifests.TryGetValue(backupId, out manifest);
        }

        foreach (var (originalPath, backupFile) in manifest ?? new List<(string OriginalPath, string BackupPath)>())
        {
            try
            {
                await Task.Run(() => File.Copy(backupFile, originalPath, true), ct);
            }
            catch (IOException ex)
            {
                LogBulkFileOperationFailed(_logger, ex, originalPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogBulkFileOperationFailed(_logger, ex, originalPath);
            }
        }
    }

    private async Task CleanupBulkBackupAsync(string backupId, CancellationToken ct)
    {
        lock (_operationsLock)
        {
            _backupManifests.Remove(backupId);
        }

        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        if (Directory.Exists(backupPath))
        {
            await Task.Run(() => Directory.Delete(backupPath, true), ct);
        }
    }
}
