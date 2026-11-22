using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// The auto-load says why it didn't register an analyzer, lists as loaded only the analyzers it registered that the
/// registry still holds, and a cancelled load leaves none of its analyzers registered
/// </summary>
public partial class AnalyzerAutoLoadServiceTests
{
    private const string UnloadedId = "Custom.Unloaded";
    private const string KeptId = "Custom.Kept";

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

    // The load registers the analyzer with UnloadedId, then the one with KeptId
    [TestCase(UnloadedId, Description = "Unloaded as soon as the registry reports it registered, before the auto-load records it")]
    [TestCase(KeptId, Description = "Unloaded later in the load, when the registry reports the next analyzer registered")]
    public async Task EnsureAnalyzersLoadedAsync_WhenAnAnalyzerItRegisteredIsUnregisteredDuringTheLoad_ReportsItInsteadOfListingIt(
        string unloadedWhenRegistered)
    {
        var unloaded = new TestAnalyzer(UnloadedId);
        var kept = new TestAnalyzer(KeptId);
        using var service = CreateServiceLoadingCustomAnalyzers(unloaded, kept);
        Task<bool>? unregistering = null;
        _registry.AnalyzerRegistered += (_, args) =>
        {
            if (args.AnalyzerId == unloadedWhenRegistered)
            {
                unregistering = _registry.UnregisterAnalyzerAsync(UnloadedId);
            }
        };

        var result = await service.EnsureAnalyzersLoadedAsync();
        var next = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(unregistering, Is.Not.Null);
        Assert.That(await unregistering!, Is.True);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.EqualTo(new IAnalyzer[] { kept }), "an analyzer the registry no longer holds isn't listed as loaded");
        Assert.That(result.Errors, Has.One.Contain(UnloadedId).And.Contain("unregistered"));
        Assert.That(service.GetStatus().AutoLoadedCount, Is.EqualTo(1));
        Assert.That(next.AlreadyLoaded, Is.True, "an analyzer that was unloaded isn't registered again");
        Assert.That(next.LoadedAnalyzers, Is.EqualTo(new IAnalyzer[] { kept }));
        Assert.That(RegisteredIds(), Is.EqualTo(new[] { KeptId }));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenAnotherAnalyzerReplacesOneItRegistered_ReportsItsOwnAndLeavesTheOther()
    {
        // e.g. load_roslyn_analyzers loads an analyzer with the ID of one that the auto-load in progress has registered
        var autoLoaded = new TestAnalyzer("Custom.Shared");
        var explicitlyLoaded = new TestAnalyzer("Custom.Shared");
        using var service = CreateServiceLoadingCustomAnalyzers(autoLoaded);
        Task<bool>? replacing = null;
        _registry.AnalyzerRegistered += (_, _) =>
        {
            if (ReferenceEquals(_registry.GetAnalyzer(autoLoaded.Id), autoLoaded))
            {
                replacing = _registry.ReplaceAnalyzerAsync(explicitlyLoaded);
            }
        };

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(replacing, Is.Not.Null);
        Assert.That(await replacing!, Is.True);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.Empty, "the analyzer the registry holds under the ID isn't the auto-load's");
        Assert.That(result.Errors, Has.One.Contain(autoLoaded.Id).And.Contain("replaced"));
        Assert.That(_registry.GetLoadedAnalyzers(), Is.EqualTo(new IAnalyzer[] { explicitlyLoaded }));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenTheLoadFailsBeforeRegisteringItsAnalyzers_ListsNone()
    {
        // The load fails once it has loaded the analyzers: it can't read the ID of one to choose those to register
        var loaded = new TestAnalyzer("Custom.Loaded");
        var withoutId = Substitute.For<IAnalyzer>();
        withoutId.Id.Returns<string>(_ => throw new InvalidOperationException("Cannot read the ID"));
        using var service = CreateServiceLoadingCustomAnalyzers(loaded, withoutId);

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.One.Contain("Cannot read the ID"));
        Assert.That(result.LoadedAnalyzers, Is.Empty, "an analyzer that was loaded but not registered isn't listed as loaded");
        Assert.That(service.GetStatus().AutoLoadedCount, Is.Zero);
        Assert.That(RegisteredIds(), Is.Empty);
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
