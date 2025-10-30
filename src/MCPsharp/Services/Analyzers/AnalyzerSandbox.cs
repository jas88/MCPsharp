using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;
using System.Runtime.Loader;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer sandbox for isolated execution
/// </summary>
public class AnalyzerSandbox : IAnalyzerSandbox
{
    private readonly ILogger<AnalyzerSandbox> _logger;
    private readonly ISecurityManager _securityManager;
    private readonly SandboxConfiguration _configuration;
    private readonly Channel<SandboxOperation> _operationQueue;
    private readonly ChannelWriter<SandboxOperation> _operationWriter;
    private readonly ChannelReader<SandboxOperation> _operationReader;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _processingTask;
    private readonly object _usageLock = new();

    private SandboxUsage _currentUsage;
    private bool _disposed;

    public AnalyzerSandbox(
        ILogger<AnalyzerSandbox> logger,
        ISecurityManager securityManager,
        SandboxConfiguration? configuration = null)
    {
        _logger = logger;
        _securityManager = securityManager;
        _configuration = configuration ?? new SandboxConfiguration();

        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _operationQueue = Channel.CreateBounded<SandboxOperation>(options);
        _operationWriter = _operationQueue.Writer;
        _operationReader = _operationQueue.Reader;
        _cancellationTokenSource = new CancellationTokenSource();

        _currentUsage = new SandboxUsage { StartTime = DateTime.UtcNow };

        _processingTask = Task.Run(ProcessOperationsAsync);
    }

    public async Task<IAnalyzer> LoadAnalyzerAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading analyzer from assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
            }

            // Load the assembly and find analyzer types
            var assemblyContext = new AssemblyLoadContext(assemblyPath, isCollectible: true);
            var assembly = assemblyContext.LoadFromAssemblyPath(assemblyPath);

            var analyzerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IAnalyzer).IsAssignableFrom(t))
                .ToList();

            if (!analyzerTypes.Any())
            {
                throw new InvalidOperationException("No analyzer types found in assembly");
            }

            // Create the first analyzer found (for simplicity)
            var analyzerType = analyzerTypes.First();
            var constructor = analyzerType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
            {
                throw new InvalidOperationException($"Analyzer type {analyzerType.Name} does not have a parameterless constructor");
            }

            var analyzer = (IAnalyzer)constructor.Invoke(Array.Empty<object>());
            await analyzer.InitializeAsync(cancellationToken);

            _logger.LogInformation("Successfully loaded analyzer: {AnalyzerId}", analyzer.Id);
            return analyzer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzer from assembly: {AssemblyPath}", assemblyPath);
            throw;
        }
    }

    public async Task<AnalysisResult> ExecuteAnalyzerAsync(IAnalyzer analyzer, string filePath, string content, CancellationToken cancellationToken = default)
    {
        var operation = new SandboxOperation
        {
            Type = SandboxOperationType.Analysis,
            AnalyzerId = analyzer.Id,
            FilePath = filePath,
            Content = content,
            Analyzer = analyzer
        };

        var resultTask = operation.TaskCompletionSource.Task;
        await _operationWriter.WriteAsync(operation, cancellationToken);

        return (AnalysisResult)await resultTask;
    }

    public async Task<FixResult> ExecuteFixAsync(IAnalyzer analyzer, ApplyFixRequest request, CancellationToken cancellationToken = default)
    {
        var operation = new SandboxOperation
        {
            Type = SandboxOperationType.Fix,
            AnalyzerId = analyzer.Id,
            FixRequest = request,
            Analyzer = analyzer
        };

        var resultTask = operation.TaskCompletionSource.Task;
        await _operationWriter.WriteAsync(operation, cancellationToken);

        return (FixResult)await resultTask;
    }

    public SandboxUsage GetUsage()
    {
        lock (_usageLock)
        {
            return _currentUsage with
            {
                EndTime = DateTime.UtcNow,
                CpuTime = DateTime.UtcNow - _currentUsage.StartTime
            };
        }
    }

    public void Reset()
    {
        lock (_usageLock)
        {
            _currentUsage = new SandboxUsage { StartTime = DateTime.UtcNow };
        }
        _logger.LogDebug("Sandbox reset");
    }

    public bool IsHealthy()
    {
        return !_disposed && !_cancellationTokenSource.IsCancellationRequested && _processingTask.IsCompleted == false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _operationWriter.Complete();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for sandbox processing task to complete");
        }

        _cancellationTokenSource.Dispose();
        _logger.LogDebug("Sandbox disposed");
    }

    private async Task ProcessOperationsAsync()
    {
        await foreach (var operation in _operationReader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var memoryBefore = GC.GetTotalMemory(false);

                switch (operation.Type)
                {
                    case SandboxOperationType.Analysis:
                        await ProcessAnalysisAsync(operation);
                        break;
                    case SandboxOperationType.Fix:
                        await ProcessFixAsync(operation);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown operation type: {operation.Type}");
                }

                stopwatch.Stop();
                var memoryAfter = GC.GetTotalMemory(false);

                UpdateUsage(stopwatch.Elapsed, memoryAfter - memoryBefore, operation.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sandbox operation: {Type}", operation.Type);
                operation.TaskCompletionSource.SetException(ex);
            }
        }
    }

    private async Task ProcessAnalysisAsync(SandboxOperation operation)
    {
        // Check permissions
        var canRead = await _securityManager.IsOperationAllowedAsync(operation.AnalyzerId, "ReadFile", operation.FilePath);
        if (!canRead)
        {
            operation.TaskCompletionSource.SetException(new UnauthorizedAccessException($"Analyzer {operation.AnalyzerId} is not allowed to read file: {operation.FilePath}"));
            return;
        }

        // Check memory usage
        var currentUsage = GetUsage();
        if (currentUsage.MemoryUsed > _configuration.MaxMemoryUsage)
        {
            operation.TaskCompletionSource.SetException(new InvalidOperationException("Memory limit exceeded"));
            return;
        }

        // Execute analysis with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        timeoutCts.CancelAfter(_configuration.MaxExecutionTime);

        try
        {
            var result = await operation.Analyzer!.AnalyzeAsync(operation.FilePath, operation.Content!, timeoutCts.Token);
            operation.TaskCompletionSource.SetResult(result);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            operation.TaskCompletionSource.SetException(new TimeoutException("Analysis timeout exceeded"));
        }
    }

    private async Task ProcessFixAsync(SandboxOperation operation)
    {
        // Check permissions for all affected files
        foreach (var issueId in operation.FixRequest!.IssueIds)
        {
            // This would need to be expanded to resolve issue IDs to file paths
            // For now, just check general write permission
            var canWrite = await _securityManager.IsOperationAllowedAsync(operation.AnalyzerId, "WriteFile");
            if (!canWrite)
            {
                operation.TaskCompletionSource.SetException(new UnauthorizedAccessException($"Analyzer {operation.AnalyzerId} is not allowed to write files"));
                return;
            }
        }

        // Execute fix with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        timeoutCts.CancelAfter(_configuration.MaxExecutionTime);

        try
        {
            // This would need the actual implementation of applying fixes
            // For now, return a placeholder result
            var result = new FixResult
            {
                Success = false,
                ErrorMessage = "Fix execution not implemented in sandbox",
                AnalyzerId = operation.AnalyzerId
            };

            operation.TaskCompletionSource.SetResult(result);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            operation.TaskCompletionSource.SetException(new TimeoutException("Fix execution timeout exceeded"));
        }
    }

    private void UpdateUsage(TimeSpan executionTime, long memoryDelta, string? filePath)
    {
        lock (_usageLock)
        {
            _currentUsage = _currentUsage with
            {
                CpuTime = _currentUsage.CpuTime.Add(executionTime),
                MemoryUsed = Math.Max(0, _currentUsage.MemoryUsed + memoryDelta),
                MemoryPeak = Math.Max(_currentUsage.MemoryPeak, _currentUsage.MemoryUsed + memoryDelta),
                FilesAccessed = !string.IsNullOrEmpty(filePath) ? _currentUsage.FilesAccessed + 1 : _currentUsage.FilesAccessed,
                EndTime = DateTime.UtcNow
            };

            // Check limits
            if (_currentUsage.MemoryUsed > _configuration.MaxMemoryUsage)
            {
                _logger.LogWarning("Sandbox memory limit exceeded: {Used} > {Limit}", _currentUsage.MemoryUsed, _configuration.MaxMemoryUsage);
            }

            if (_currentUsage.FilesAccessed > _configuration.MaxFileAccess)
            {
                _logger.LogWarning("Sandbox file access limit exceeded: {Accessed} > {Limit}", _currentUsage.FilesAccessed, _configuration.MaxFileAccess);
            }
        }
    }

    private class SandboxOperation
    {
        public SandboxOperationType Type { get; init; }
        public string AnalyzerId { get; init; } = string.Empty;
        public string? FilePath { get; init; }
        public string? Content { get; init; }
        public ApplyFixRequest? FixRequest { get; init; }
        public IAnalyzer? Analyzer { get; init; }
        public TaskCompletionSource<object> TaskCompletionSource { get; } = new();
    }

    private enum SandboxOperationType
    {
        Analysis,
        Fix
    }
}