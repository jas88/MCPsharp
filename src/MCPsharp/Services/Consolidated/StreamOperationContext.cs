using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Stream operation context
/// </summary>
internal sealed class StreamOperationContext
{
    public required string Id { get; init; }
    public required StreamOperationType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public StreamStatus Status { get; set; }
    public string? ProgressId { get; set; }
    public string? ProcessorType { get; set; }
    public string? CurrentFile { get; set; }
    public long BytesProcessed { get; set; }
    public long BytesRead { get; set; }
    public long BytesWritten { get; set; }
    public long RecordsProcessed { get; set; }
    public int ChunksProcessed { get; set; }
    public TimeSpan ElapsedTime => DateTime.UtcNow - CreatedAt;
    public double AverageProcessingRate { get; set; }
    public long PeakMemoryUsage { get; set; }
    public double ProcessorUtilization { get; set; }
    public TimeSpan IOWaitTime { get; set; }

    private const int MaxRetainedLogEntries = 200;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logEntries = new();

    /// <summary>
    /// Records a timestamped log entry for this operation, retaining only the most recent entries
    /// </summary>
    public void AddLog(string message)
    {
        _logEntries.Enqueue($"{DateTime.UtcNow:O} {message}");
        while (_logEntries.Count > MaxRetainedLogEntries && _logEntries.TryDequeue(out _))
        {
            // Trim oldest entries
        }
    }

    /// <summary>
    /// Returns up to <paramref name="maxEntries"/> of the most recent log entries, oldest first
    /// </summary>
    public List<string> GetRecentLogs(int maxEntries)
    {
        var snapshot = _logEntries.ToArray();
        return snapshot.Skip(Math.Max(0, snapshot.Length - maxEntries)).ToList();
    }
}
