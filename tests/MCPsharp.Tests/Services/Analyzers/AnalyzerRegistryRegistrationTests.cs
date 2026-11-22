using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry registers an analyzer only once it has initialized, and disposes one it initialized but didn't
/// register
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerRegistryRegistrationTests : TestBase
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
    public async Task RegisterAnalyzerAsync_WhileTheAnalyzerInitializes_DoesNotRegisterItYet()
    {
        // An analyzer that hasn't initialized can't be got and run yet
        var analyzer = new TestAnalyzer(AnalyzerId, holdInitialization: true);

        var registering = _registry.RegisterAnalyzerAsync(analyzer);
        await analyzer.WaitUntilInitializingAsync();

        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        Assert.That(_registry.GetRegisteredAnalyzers(), Is.Empty);
        analyzer.ReleaseInitialization();
        Assert.That(await registering, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(analyzer));
    }

    [Test]
    public async Task RegisterAnalyzerAsync_WithAnAnalyzerThatFailsToInitialize_ThrowsDoesNotRegisterItAndDisposesIt()
    {
        // The failure propagates, so a caller can tell it from a rejection of the ID
        var failing = new TestAnalyzer(AnalyzerId, initializationError: new InvalidOperationException("Cannot initialize"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _registry.RegisterAnalyzerAsync(failing));

        Assert.That(error.Message, Is.EqualTo("Cannot initialize"));
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        await failing.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task RegisterAnalyzerAsync_CancelledWhileTheAnalyzerInitializes_ThrowsDoesNotRegisterItAndDisposesIt()
    {
        var analyzer = new TestAnalyzer(AnalyzerId, holdInitialization: true);
        using var cancellation = new CancellationTokenSource();
        var registering = _registry.RegisterAnalyzerAsync(analyzer, cancellation.Token);
        await analyzer.WaitUntilInitializingAsync();

        await cancellation.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(() => registering);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        await analyzer.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task RegisterAnalyzerAsync_WithACancelledToken_ThrowsEvenForAnAnalyzerThatIgnoresTheToken()
    {
        // e.g. a Roslyn analyzer adapter, whose initialization doesn't look at the token
        var analyzer = new TestAnalyzer(AnalyzerId);

        await Assert.CatchAsync<OperationCanceledException>(() => _registry.RegisterAnalyzerAsync(analyzer, new CancellationToken(canceled: true)));

        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
    }

    [Test]
    public async Task RegisterAnalyzerAsync_WithTheIdRegistered_ReturnsFalse()
    {
        var registered = new TestAnalyzer(AnalyzerId);
        var other = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(registered);

        Assert.That(await _registry.RegisterAnalyzerAsync(other), Is.False);

        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(registered));
    }

    [Test]
    public async Task RegisterAnalyzerAsync_WhenAnotherAnalyzerTakesTheIdWhileItInitializes_DoesNotRegisterItAndDisposesIt()
    {
        var slower = new TestAnalyzer(AnalyzerId, holdInitialization: true);
        var faster = new TestAnalyzer(AnalyzerId);
        var registering = _registry.RegisterAnalyzerAsync(slower);
        await slower.WaitUntilInitializingAsync();

        Assert.That(await _registry.RegisterAnalyzerAsync(faster), Is.True);
        slower.ReleaseInitialization();

        Assert.That(await registering, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(faster));
        await slower.WaitUntilDisposedAsync();
        Assert.That(await faster.IsDisposedSoonAsync(), Is.False);
    }
}
