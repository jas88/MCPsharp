using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

public partial class StreamOperationManager
{
    public async Task<CheckpointData?> CreateCheckpointAsync(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return null;
        }

        try
        {
            var checkpoint = await _streamingProcessor.CreateCheckpointAsync(operationId);
            if (checkpoint != null)
            {
                // Add to operation's checkpoint list
                var checkpoints = _checkpoints.GetOrAdd(operationId, _ => new List<CheckpointData>());
                lock (checkpoints)
                {
                    checkpoints.Add(checkpoint);
                    // Keep only last 10 checkpoints per operation
                    if (checkpoints.Count > 10)
                    {
                        checkpoints.RemoveAt(0);
                    }
                }

                operation.LastCheckpoint = checkpoint;
                operation.LastCheckpointAt = DateTime.UtcNow;

                LogCreatedCheckpointForOperation(_logger, checkpoint.CheckpointId, operationId);
            }

            return checkpoint;
        }
        catch (Exception ex)
        {
            LogErrorCreatingCheckpointForOperation(_logger, ex, operationId);
            return null;
        }
    }

    public async Task<StreamResult?> ResumeFromCheckpointAsync(string operationId, string checkpointId,
        CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return null;
        }

        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return null;
        }

        CheckpointData? checkpoint = null;
        lock (checkpoints)
        {
            checkpoint = checkpoints.FirstOrDefault(c => c.CheckpointId == checkpointId);
        }

        if (checkpoint == null)
        {
            return null;
        }

        try
        {
            operation.Status = StreamOperationStatus.Resumed;

            LogResumingOperationFromCheckpoint(_logger, operationId, checkpointId);

            var result = await _streamingProcessor.ResumeOperationAsync(operationId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            LogErrorResumingOperationFromCheckpoint(_logger, ex, operationId, checkpointId);
            operation.Status = StreamOperationStatus.Failed;
            operation.ErrorMessage = ex.Message;
            operation.Exception = ex;
            return null;
        }
    }

    public Task<List<CheckpointData>> GetCheckpointsAsync(string operationId)
    {
        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return Task.FromResult(new List<CheckpointData>());
        }

        lock (checkpoints)
        {
            return Task.FromResult(checkpoints.OrderByDescending(c => c.CreatedAt).ToList());
        }
    }

    public Task<bool> DeleteCheckpointAsync(string operationId, string checkpointId)
    {
        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return Task.FromResult(false);
        }

        lock (checkpoints)
        {
            var checkpoint = checkpoints.FirstOrDefault(c => c.CheckpointId == checkpointId);
            if (checkpoint == null)
            {
                return Task.FromResult(false);
            }

            // Delete checkpoint file
            try
            {
                if (File.Exists(checkpoint.CheckpointFilePath))
                {
                    File.Delete(checkpoint.CheckpointFilePath);
                }
            }
            catch (Exception ex)
            {
                LogErrorDeletingCheckpointFile(_logger, ex, checkpoint.CheckpointFilePath);
            }

            // Remove from list
            checkpoints.Remove(checkpoint);
            return Task.FromResult(true);
        }
    }
}
