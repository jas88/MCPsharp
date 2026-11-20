using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// The auto-load says why it didn't register an analyzer, and a cancelled load leaves none of its analyzers
/// registered
/// </summary>
public partial class AnalyzerAutoLoadServiceTests
{
    /// <summary>
    /// A service whose configuration's one custom path is an assembly that holds <paramref name="analyzers"/>
    /// </summary>
    private AnalyzerAutoLoadService CreateServiceLoadingCustomAnalyzers(params IAnalyzer[] analyzers)
    {
        ProjectsReference();
        var assemblyPath = CreateTestFile("not read: the loader returns the analyzers", ".dll");
        return CreateService(
            new AnalyzerAutoLoadConfiguration { CustomAnalyzerPaths = [assemblyPath] },
            new CustomAssemblyLoader(assemblyPath, analyzers));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAnAnalyzerThatFailsToInitialize_ReportsTheFailureAndLoadsTheOthers()
    {
        var failing = new TestAnalyzer("Custom.Failing", initializationError: new InvalidOperationException("Cannot initialize"));
        var working = new TestAnalyzer("Custom.Working");
        using var service = CreateServiceLoadingCustomAnalyzers(failing, working);

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.EqualTo(new IAnalyzer[] { working }));
        Assert.That(result.Errors, Has.One.Contain(failing.Id).And.Contain("Failed to initialize").And.Contain("Cannot initialize"));
        Assert.That(result.Errors, Has.None.Contain("already registered"));
        Assert.That(RegisteredIds(), Is.EqualTo(new[] { working.Id }));
        await failing.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAnAnalyzerThatCancelsItsOwnInitialization_ReportsItAsFailedToInitialize()
    {
        // The load itself isn't cancelled, so it goes on
        var failing = new TestAnalyzer("Custom.Failing", initializationError: new OperationCanceledException("Timed out"));
        var working = new TestAnalyzer("Custom.Working");
        using var service = CreateServiceLoadingCustomAnalyzers(failing, working);

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.EqualTo(new IAnalyzer[] { working }));
        Assert.That(result.Errors, Has.One.Contain(failing.Id).And.Contain("Failed to initialize").And.Contain("Timed out"));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_CancelledWhileAnAnalyzerInitializes_ThrowsAndLeavesNoneOfTheLoadRegistered()
    {
        var first = new TestAnalyzer("Custom.First");
        var held = new TestAnalyzer("Custom.Held", holdInitialization: true);
        using var service = CreateServiceLoadingCustomAnalyzers(first, held);
        using var cancellation = new CancellationTokenSource();
        var loading = service.EnsureAnalyzersLoadedAsync(cancellation.Token);
        await held.WaitUntilInitializingAsync();

        await cancellation.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(() => loading);
        Assert.That(RegisteredIds(), Is.Empty, "the analyzers the cancelled load registered are unregistered");
        Assert.That(service.GetStatus().HasAutoLoaded, Is.False, "nothing is recorded as loaded, so the next call loads");
        await first.WaitUntilDisposedAsync();
        await held.WaitUntilDisposedAsync();
    }

    /// <summary>
    /// A loader that finds <paramref name="analyzers"/> in the assembly at <paramref name="customAssemblyPath"/>, and
    /// nothing anywhere else
    /// </summary>
    private sealed class CustomAssemblyLoader(string customAssemblyPath, IAnalyzer[] analyzers)
        : RoslynAnalyzerLoader(NullLogger<RoslynAnalyzerLoader>.Instance, NullLoggerFactory.Instance)
    {
        public override Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromAssemblyAsync(
            string assemblyPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(assemblyPath == customAssemblyPath ? analyzers.ToImmutableArray() : ImmutableArray<IAnalyzer>.Empty);
    }
}
