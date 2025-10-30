using System.Collections.Immutable;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// Sandbox for isolated analyzer execution
/// </summary>
public interface IAnalyzerSandbox : IDisposable
{
    /// <summary>
    /// Execute an analyzer in isolation
    /// </summary>
    Task<AnalysisResult> ExecuteAnalyzerAsync(IAnalyzer analyzer, string filePath, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a fix in isolation
    /// </summary>
    Task<FixResult> ExecuteFixAsync(IAnalyzer analyzer, ApplyFixRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get resource usage statistics
    /// </summary>
    SandboxUsage GetUsage();

    /// <summary>
    /// Reset the sandbox state
    /// </summary>
    void Reset();

    /// <summary>
    /// Check if the sandbox is healthy
    /// </summary>
    bool IsHealthy();
}

/// <summary>
/// Resource usage statistics for sandbox
/// </summary>
public record SandboxUsage
{
    public TimeSpan CpuTime { get; init; }
    public long MemoryUsed { get; init; }
    public long MemoryPeak { get; init; }
    public int FilesAccessed { get; init; }
    public int NetworkRequests { get; init; }
    public int ProcessesCreated { get; init; }
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for sandbox execution
/// </summary>
public record SandboxConfiguration
{
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromMinutes(5);
    public long MaxMemoryUsage { get; init; } = 512 * 1024 * 1024; // 512MB
    public int MaxFileAccess { get; init; } = 1000;
    public bool AllowNetworkAccess { get; init; } = false;
    public bool AllowProcessCreation { get; init; } = false;
    public ImmutableArray<string> AllowedPaths { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> DeniedPaths { get; init; } = ImmutableArray<string>.Empty;
}