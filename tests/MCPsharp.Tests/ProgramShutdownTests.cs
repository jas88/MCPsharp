using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests;

/// <summary>
/// Shutdown stops the analyzer load that loadOnStartup started, however the server loop ends, and a failed load
/// doesn't make the shutdown fail
/// </summary>
[TestFixture]
public class ProgramShutdownTests
{
    [Test]
    public async Task RunUntilStoppedAsync_WhenCtrlCEndsTheServerLoop_StopsThePreloadAndWaitsForIt()
    {
        using var cts = new CancellationTokenSource();
        var preload = Task.Run(() => Task.Delay(Timeout.Infinite, cts.Token));

        // Ctrl+C cancels the token, and the server loop throws as it is reading a request
        Task RunServerAsync()
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => Program.RunUntilStoppedAsync(RunServerAsync, preload, cts, NullLogger.Instance));
        Assert.That(preload.IsCompleted, Is.True, "The preload should have ended before the server's services are disposed");
    }

    [Test]
    public async Task RunUntilStoppedAsync_WhenTheServerLoopEnds_StopsThePreload()
    {
        using var cts = new CancellationTokenSource();
        var preload = Task.Run(() => Task.Delay(Timeout.Infinite, cts.Token));

        await Program.RunUntilStoppedAsync(() => Task.CompletedTask, preload, cts, NullLogger.Instance);

        Assert.That(preload.IsCanceled, Is.True);
    }

    [Test]
    public async Task StopAnalyzerPreloadAsync_WithAFailedPreload_LogsTheFailureInsteadOfThrowing()
    {
        using var cts = new CancellationTokenSource();
        var logger = new ListLogger();
        var failure = new InvalidOperationException("The preload failed");

        await Program.StopAnalyzerPreloadAsync(Task.FromException(failure), cts, logger);

        Assert.That(logger.Entries, Has.One.Matches<(LogLevel Level, Exception? Exception)>(e => e.Level == LogLevel.Warning && ReferenceEquals(e.Exception, failure)));
    }

    /// <summary>
    /// A logger that keeps the level and exception of each entry
    /// </summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
