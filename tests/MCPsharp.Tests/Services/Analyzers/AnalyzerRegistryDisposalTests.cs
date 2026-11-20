using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry disposes every analyzer it stops holding, whatever the state of the caller's cancellation token
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerRegistryDisposalTests : TestBase
{
    private const string AnalyzerId = "analyzer1";

    private AnalyzerRegistry _registry = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
    }

    private static CancellationToken CancelledToken() => new(canceled: true);

    [Test]
    public async Task UnregisterAnalyzerAsync_WithACancelledToken_StillDisposesTheAnalyzer()
    {
        var analyzer = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(analyzer);

        Assert.That(await _registry.UnregisterAnalyzerAsync(AnalyzerId, CancelledToken()), Is.True);

        await analyzer.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task UnregisterAnalyzerInstanceAsync_WithACancelledToken_StillDisposesTheAnalyzer()
    {
        // e.g. a cancelled auto-load unregistering the analyzers of its previous load
        var analyzer = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(analyzer);

        Assert.That(await _registry.UnregisterAnalyzerInstanceAsync(analyzer, CancelledToken()), Is.True);

        await analyzer.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_CancelledAfterTheReplacementStartsInitializing_DisposesTheReplacedAnalyzer()
    {
        // The replacement ignores the cancellation and initializes, so it replaces the other analyzer
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId, holdInitialization: true, initializationObservesCancellation: false);
        await _registry.RegisterAnalyzerAsync(previous);
        using var cancellation = new CancellationTokenSource();
        var replacing = _registry.ReplaceAnalyzerAsync(replacement, cancellation.Token);
        await replacement.WaitUntilInitializingAsync();

        await cancellation.CancelAsync();
        replacement.ReleaseInitialization();

        Assert.That(await replacing, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement));
        await previous.WaitUntilDisposedAsync();
    }
}
