using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class StreamProcessingController
{
    /// <summary>
    /// stream_monitor - Monitor stream operations
    /// Consolidates: Monitoring tools
    /// </summary>
    public async Task<StreamMonitorResponse> MonitorStreamAsync(string operationId, StreamMonitorRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {

            // Get operation context
            StreamOperationContext? context;
            lock (_streamsLock)
            {
                _activeStreams.TryGetValue(operationId, out context);
            }

            if (context == null)
            {
                return new StreamMonitorResponse
                {
                    OperationId = operationId,
                    Status = StreamStatus.NotFound,
                    CreatedAt = DateTime.UtcNow,
                    RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false },
                    Error = $"Stream operation {operationId} not found"
                };
            }

            // Build response data
            StreamProgressInfo? progressInfo = null;
            StreamMetrics? metrics = null;
            List<string>? recentLogs = null;
            StreamPerformanceData? performance = null;

            // Get progress from tracker
            if (_progressTracker != null && context.ProgressId != null)
            {
                var progress = await _progressTracker.GetProgressAsync(context.ProgressId);
                if (progress != null)
                {
                    // Extract progress info from metadata
                    progress.Metadata.TryGetValue("TotalSteps", out var totalStepsObj);
                    progress.Metadata.TryGetValue("CurrentStep", out var currentStepObj);
                    progress.Metadata.TryGetValue("CurrentItem", out var currentItemObj);
                    progress.Metadata.TryGetValue("Message", out var messageObj);
                    progress.Metadata.TryGetValue("PercentComplete", out var percentCompleteObj);

                    var totalSteps = totalStepsObj as int? ?? 0;
                    var currentStep = currentStepObj as int? ?? 0;
                    var currentItem = currentItemObj as string ?? string.Empty;
                    var message = messageObj as string ?? string.Empty;
                    var percentComplete = percentCompleteObj as double? ?? 0.0;

                    progressInfo = new StreamProgressInfo
                    {
                        CurrentStep = currentStep,
                        TotalSteps = totalSteps,
                        PercentComplete = percentComplete,
                        CurrentFile = currentItem,
                        ElapsedTime = DateTime.UtcNow - progress.LastUpdated,
                        EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
                        Message = message,
                        ProcessingRate = CalculateProcessingRate(context),
                        MemoryUsage = GetMemoryUsage(),
                        CpuUsage = GetCpuUsage()
                    };
                }
            }

            // Get detailed metrics
            metrics = GetStreamMetrics(context);

            // Get recent logs
            if (request.IncludeLogs)
            {
                recentLogs = GetRecentStreamLogs(context, request.MaxLogEntries ?? 50);
            }

            // Get performance data
            if (request.IncludePerformance)
            {
                performance = GetStreamPerformance(context);
            }

            stopwatch.Stop();

            return new StreamMonitorResponse
            {
                OperationId = operationId,
                Status = context.Status,
                CreatedAt = context.CreatedAt,
                StartedAt = context.StartedAt,
                EstimatedCompletion = context.EstimatedCompletion,
                Progress = progressInfo,
                Metrics = metrics,
                Performance = performance,
                RecentLogs = recentLogs,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = true
                }
            };
        }
        catch (Exception ex)
        {
            LogErrorMonitoringStream(_logger, ex, operationId);

            return new StreamMonitorResponse
            {
                OperationId = operationId,
                Status = StreamStatus.Failed,
                CreatedAt = DateTime.UtcNow,
                Error = ex.Message,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    private static StreamMetrics GetStreamMetrics(StreamOperationContext context)
    {
        // Implementation for gathering stream metrics
        return new StreamMetrics
        {
            BytesRead = context.BytesRead,
            BytesWritten = context.BytesWritten,
            RecordsProcessed = context.RecordsProcessed,
            ChunksProcessed = context.ChunksProcessed,
            AverageProcessingRate = context.AverageProcessingRate,
            PeakMemoryUsage = context.PeakMemoryUsage,
            ProcessorUtilization = context.ProcessorUtilization,
            IOWaitTime = context.IOWaitTime
        };
    }

    private static double CalculateProcessingRate(StreamOperationContext context)
    {
        if (context.ElapsedTime.TotalSeconds == 0)
            return 0;

        return context.BytesProcessed / context.ElapsedTime.TotalSeconds;
    }

    private static long GetMemoryUsage()
    {
        // Implementation for getting memory usage
        return GC.GetTotalMemory(false);
    }

    private static double GetCpuUsage()
    {
        // Average CPU utilisation of this process since it started, as a percentage of all cores
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var wallClock = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        if (wallClock <= TimeSpan.Zero)
        {
            return 0.0;
        }

        return process.TotalProcessorTime.TotalMilliseconds / (wallClock.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
    }

    private static StreamPerformanceData GetStreamPerformance(StreamOperationContext context)
    {
        // Implementation for performance data
        return new StreamPerformanceData
        {
            AverageThroughput = context.AverageProcessingRate,
            PeakThroughput = context.AverageProcessingRate * 1.5, // Estimate peak as 1.5x average
            AverageLatency = TimeSpan.FromMilliseconds(100), // Placeholder
            PeakLatency = TimeSpan.FromMilliseconds(200), // Placeholder
            CpuUtilization = GetCpuUsage(),
            MemoryUsage = GetMemoryUsage(),
            ThreadUtilization = 1, // Placeholder
            DiskReadOps = 0, // Placeholder
            DiskWriteOps = 0, // Placeholder
            DiskReadBytes = context.BytesRead,
            DiskWriteBytes = context.BytesWritten
        };
    }

    private static List<string> GetRecentStreamLogs(StreamOperationContext context, int maxEntries)
    {
        return context.GetRecentLogs(maxEntries);
    }
}
