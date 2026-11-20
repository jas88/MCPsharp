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
    public async Task RegisterAnalyzerAsync_WithAnAnalyzerThatFailsToInitialize_DoesNotRegisterItAndDisposesIt()
    {
        var failing = new TestAnalyzer(AnalyzerId, initializationError: new InvalidOperationException("Cannot initialize"));

        var registered = await _registry.RegisterAnalyzerAsync(failing);

        Assert.That(registered, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        await failing.WaitUntilDisposedAsync();
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
        Assert.That(faster.IsDisposed, Is.False);
    }
}
