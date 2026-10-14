using System.Globalization;
using MCPsharp.Models.BulkEdit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// Helper methods for file resolution, condition checking, and utility functions
/// </summary>
public sealed partial class BulkEditService
{
    /// <summary>
    /// Timeout applied to user-supplied regular expressions evaluated by bulk edit helpers.
    /// </summary>
    private static readonly TimeSpan BulkEditRegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Non-null logger for source-generated log methods (falls back to a no-op logger).
    /// </summary>
    private ILogger BulkEditLog => (ILogger?)_logger ?? NullLogger.Instance;

    /// <summary>
    /// Render bulk edit statistics as a human-readable summary line.
    /// </summary>
    private static string FormatBulkEditSummary(BulkEditSummary summary)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"Processed {summary.TotalFilesProcessed} of {summary.TotalFilesMatched} files: " +
            $"{summary.SuccessfulFiles} succeeded, {summary.FailedFiles} failed, {summary.SkippedFiles} skipped, " +
            $"{summary.TotalChangesApplied} changes applied");
    }

    /// <summary>
    /// Render preview statistics as a human-readable summary line.
    /// </summary>
    private static string FormatPreviewSummary(PreviewSummary summary)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{summary.FilesToChange} of {summary.TotalFiles} files would change ({summary.FilesToSkip} skipped): " +
            $"{summary.TotalChanges} changes, +{summary.LinesAdded}/-{summary.LinesRemoved} lines, " +
            $"{summary.SizeChange:+#;-#;0} characters");
    }

    private static BulkEditResult CreateErrorResult(string operationId, DateTime startTime, string error)
    {
        var errors = new List<BulkEditError>
        {
            new BulkEditError
            {
                FilePath = "Error occurred",
                ErrorMessage = error,
                Timestamp = DateTime.UtcNow
            }
        };

        return new BulkEditResult
        {
            Success = false,
            TotalFiles = 0,
            ModifiedFiles = 0,
            SkippedFiles = 0,
            FailedFiles = 1,
            FileResults = Array.Empty<FileBulkEditResult>(),
            Errors = errors,
            ElapsedTime = DateTime.UtcNow - startTime,
            Summary = $"Operation failed: {error}",
            Error = error,
            OperationId = operationId,
            StartTime = startTime,
            EndTime = DateTime.UtcNow
        };
    }
}
