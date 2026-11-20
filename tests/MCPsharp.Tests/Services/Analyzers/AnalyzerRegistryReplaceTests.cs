using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry replaces an analyzer, and unregisters a particular one, in one step, and registers a replacement
/// only once it has initialized
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerRegistryReplaceTests : TestBase
{
    private const string AnalyzerId = "analyzer1";

    private AnalyzerRegistry _registry = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_RegistersTheAnalyzerInPlaceOfTheOneWithItsId()
    {
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(previous);

        var replaced = await _registry.ReplaceAnalyzerAsync(replacement);

        Assert.That(replaced, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement));
        Assert.That(_registry.GetRegisteredAnalyzers(), Has.Length.EqualTo(1));
        await previous.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_WhileTheReplacementInitializes_KeepsTheOneItIsToReplace()
    {
        // A replacement that hasn't initialized can't be got and run yet
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId, holdInitialization: true);
        await _registry.RegisterAnalyzerAsync(previous);

        var replacing = _registry.ReplaceAnalyzerAsync(replacement);
        await replacement.WaitUntilInitializingAsync();

        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(previous));
        Assert.That(_registry.GetLoadedAnalyzers(), Is.EqualTo(new IAnalyzer[] { previous }));
        replacement.ReleaseInitialization();
        Assert.That(await replacing, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement));
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_WithAnAnalyzerThatFailsToInitialize_KeepsTheOneItWasToReplaceAndDisposesIt()
    {
        var previous = new TestAnalyzer(AnalyzerId);
        var failing = new TestAnalyzer(AnalyzerId, initializationError: new InvalidOperationException("Cannot initialize"));
        await _registry.RegisterAnalyzerAsync(previous);

        var replaced = await _registry.ReplaceAnalyzerAsync(failing);

        Assert.That(replaced, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(previous));
        await failing.WaitUntilDisposedAsync();
        Assert.That(previous.IsDisposed, Is.False);
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_CancelledWhileTheReplacementInitializes_KeepsTheOneItWasToReplaceAndDisposesIt()
    {
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId, holdInitialization: true);
        await _registry.RegisterAnalyzerAsync(previous);
        using var cancellation = new CancellationTokenSource();
        var replacing = _registry.ReplaceAnalyzerAsync(replacement, cancellation.Token);
        await replacement.WaitUntilInitializingAsync();

        await cancellation.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(() => replacing);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(previous));
        await replacement.WaitUntilDisposedAsync();
        Assert.That(previous.IsDisposed, Is.False);
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_WhenTheIdIsUnregisteredWhileTheReplacementInitializes_RegistersTheReplacement()
    {
        // e.g. the ID is unloaded meanwhile, which unloads the analyzer registered under it then
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId, holdInitialization: true);
        await _registry.RegisterAnalyzerAsync(previous);
        IAnalyzer? registeredWhenReported = null;
        _registry.AnalyzerRegistered += (_, args) => registeredWhenReported = _registry.GetAnalyzer(args.AnalyzerId);
        var replacing = _registry.ReplaceAnalyzerAsync(replacement);
        await replacement.WaitUntilInitializingAsync();

        Assert.That(await _registry.UnregisterAnalyzerAsync(AnalyzerId), Is.True);
        replacement.ReleaseInitialization();

        Assert.That(await replacing, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement), "a replacement reported registered is registered");
        Assert.That(registeredWhenReported, Is.SameAs(replacement), "the registration event is for an analyzer that is registered");
        await previous.WaitUntilDisposedAsync();
        Assert.That(replacement.IsDisposed, Is.False);
    }

    [Test]
    public async Task UnregisterAnalyzerInstanceAsync_LeavesAnAnalyzerThatHasReplacedIt()
    {
        // e.g. the auto-load unregistering its analyzer after load_roslyn_analyzers has replaced it
        var autoLoaded = new TestAnalyzer(AnalyzerId);
        var explicitlyLoaded = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(autoLoaded);
        await _registry.ReplaceAnalyzerAsync(explicitlyLoaded);

        var unregistered = await _registry.UnregisterAnalyzerInstanceAsync(autoLoaded);

        Assert.That(unregistered, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(explicitlyLoaded));
        Assert.That(await _registry.UnregisterAnalyzerInstanceAsync(explicitlyLoaded), Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
    }
}
