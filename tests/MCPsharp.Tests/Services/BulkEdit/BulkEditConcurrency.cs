using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Tests.Services.BulkEdit;

/// <summary>
/// How many files bulk edits processed at once, from when each file's processing started and ended
/// </summary>
internal static class BulkEditConcurrency
{
    /// <summary>
    /// The most files whose processing was in progress at the same moment
    /// </summary>
    public static int MostFilesInProgress(IEnumerable<FileBulkEditResult> files)
    {
        // A file that started as another ended did not overlap it, so at the same time ends come before starts
        var events = files
            .SelectMany(file => new[] { (Time: file.ProcessStartTime, Change: 1), (Time: file.ProcessEndTime, Change: -1) })
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Change);

        var inProgress = 0;
        var most = 0;
        foreach (var (_, change) in events)
        {
            inProgress += change;
            most = Math.Max(most, inProgress);
        }

        return most;
    }
}
