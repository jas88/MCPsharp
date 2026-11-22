using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry publishes the analyzers it holds in one step: a caller that keeps its own record of the registered
/// analyzers updates it while nothing can be registered or unregistered
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerRegistryPublishTests : TestBase
{
    // Long enough for an unregistration that nothing holds up to finish
    private static readonly TimeSpan UnregistrationWait = TimeSpan.FromMilliseconds(250);

    private AnalyzerRegistry _registry = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
    }

    [Test]
    public async Task PublishRegisteredAnalyzers_PublishesOnlyTheAnalyzersTheRegistryHolds()
    {
        var registered = new TestAnalyzer("registered");
        var replaced = new TestAnalyzer("replaced");
        var replacement = new TestAnalyzer("replaced");
        var unregistered = new TestAnalyzer("unregistered");
        var neverRegistered = new TestAnalyzer("never-registered");
        await _registry.RegisterAnalyzerAsync(registered);
        await _registry.RegisterAnalyzerAsync(replaced);
        await _registry.ReplaceAnalyzerAsync(replacement);
        await _registry.RegisterAnalyzerAsync(unregistered);
        await _registry.UnregisterAnalyzerAsync(unregistered.Id);
        IReadOnlyList<IAnalyzer>? published = null;

        var returned = _registry.PublishRegisteredAnalyzers(
            [registered, replaced, unregistered, neverRegistered],
            analyzers => published = analyzers);

        Assert.That(published, Is.EqualTo(new IAnalyzer[] { registered }));
        Assert.That(returned, Is.EqualTo(new IAnalyzer[] { registered }));
    }

    [Test]
    public async Task PublishRegisteredAnalyzers_WhileItPublishes_KeepsTheAnalyzersRegistered()
    {
        // So that what the caller publishes is what the registry holds when it has finished publishing
        var analyzer = new TestAnalyzer("analyzer1");
        await _registry.RegisterAnalyzerAsync(analyzer);
        Task<bool>? unregistering = null;
        var unregisteredWhilePublishing = true;
        IAnalyzer? heldWhilePublishing = null;

        _registry.PublishRegisteredAnalyzers([analyzer], _ =>
        {
            unregistering = Task.Run(() => _registry.UnregisterAnalyzerAsync(analyzer.Id));
            unregisteredWhilePublishing = unregistering.Wait(UnregistrationWait);
            heldWhilePublishing = _registry.GetAnalyzer(analyzer.Id);
        });

        Assert.That(unregisteredWhilePublishing, Is.False, "an unregistration waits until the analyzers are published");
        Assert.That(heldWhilePublishing, Is.SameAs(analyzer));
        Assert.That(await unregistering!, Is.True);
        Assert.That(_registry.GetAnalyzer(analyzer.Id), Is.Null);
    }
}
