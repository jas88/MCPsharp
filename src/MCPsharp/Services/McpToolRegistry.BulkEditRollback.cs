using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Bulk edit rollback tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Rollback a previous bulk edit operation using created backups
    /// </summary>
    /// <param name="arguments">Tool arguments containing rollbackId</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with rollback details</returns>
    private async Task<ToolCallResult> ExecuteRollbackBulkEdit(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var rollbackId = arguments.RootElement.GetProperty("rollbackId").GetString();

            if (string.IsNullOrEmpty(rollbackId))
            {
                return new ToolCallResult { Success = false, Error = "Rollback ID is required" };
            }

            var result = await _bulkEditService.RollbackBulkEditAsync(rollbackId, cancellationToken);
            await SyncWorkspaceWithRestoredFilesAsync(result, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteRollbackBulkEdit));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Reloads every file a rollback restored into the Roslyn workspace, so workspace-based tools
    /// see the restored text rather than the edited text. Files that are not part of the loaded
    /// solution are ignored; dependent projects are refreshed by <see cref="Roslyn.RoslynWorkspace.UpdateDocumentAsync"/>.
    /// </summary>
    private async Task SyncWorkspaceWithRestoredFilesAsync(BulkEditResult rollbackResult, CancellationToken cancellationToken)
    {
        if (_workspace == null)
        {
            return;
        }

        var restoredPaths = rollbackResult.FileResults
            .Where(r => r.Success && !r.Skipped)
            .Select(r => r.FilePath)
            .Where(File.Exists);

        foreach (var path in restoredPaths)
        {
            var restoredText = await File.ReadAllTextAsync(path, cancellationToken);
            await _workspace.UpdateDocumentAsync(path, restoredText, cancellationToken);
        }
    }

    /// <summary>
    /// Get list of available rollback sessions that can be used to undo changes
    /// </summary>
    /// <returns>Tool execution result with list of available rollbacks</returns>
    private async Task<ToolCallResult> ExecuteGetAvailableRollbacks(CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var rollbacks = await _bulkEditService.GetAvailableRollbacksAsync(cancellationToken);

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    rollbacks = rollbacks.Select(r => new
                    {
                        rollbackId = r.RollbackId,
                        operationType = r.OperationType,
                        timestamp = r.Timestamp,
                        filesAffected = r.FilesAffected,
                        changesCount = r.ChangesCount,
                        description = r.Description
                    }).ToArray(),
                    count = rollbacks.Count
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetAvailableRollbacks));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}
