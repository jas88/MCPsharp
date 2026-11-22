using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry tells every subscriber of an event, and a subscriber that throws changes neither what the registry
/// did nor what it reports: an exception from registering means the analyzer was not registered
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerRegistryEventTests : TestBase
{
    private const string AnalyzerId = "analyzer1";
    private static readonly string[] ToldOfTheAnalyzer = [AnalyzerId];

    private AnalyzerRegistry _registry = null!;
    private List<string> _told = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        _told = [];
    }

    /// <summary>
    /// Subscribe to the registration event twice: a subscriber that throws, then one that records what it is told
    /// </summary>
    private void SubscribeToRegistrations()
    {
        _registry.AnalyzerRegistered += (_, _) => throw new InvalidOperationException("The subscriber failed");
        _registry.AnalyzerRegistered += (_, args) => _told.Add(args.AnalyzerId);
    }

    /// <summary>
    /// Subscribe to the unregistration event twice: a subscriber that throws, then one that records what it is told
    /// </summary>
    private void SubscribeToUnregistrations()
    {
        _registry.AnalyzerUnregistered += (_, _) => throw new InvalidOperationException("The subscriber failed");
        _registry.AnalyzerUnregistered += (_, args) => _told.Add(args.AnalyzerId);
    }

    [Test]
    public async Task RegisterAnalyzerAsync_WhenASubscriberThrows_ReportsTheAnalyzerRegisteredAndTellsTheOthers()
    {
        // Its exception would look like the analyzer failing to initialize, which leaves an analyzer unregistered
        var analyzer = new TestAnalyzer(AnalyzerId);
        SubscribeToRegistrations();

        var registered = await _registry.RegisterAnalyzerAsync(analyzer);

        Assert.That(registered, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(analyzer));
        Assert.That(_told, Is.EqualTo(ToldOfTheAnalyzer));
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_WhenASubscriberThrows_ReportsTheAnalyzerRegisteredAndTellsTheOthers()
    {
        var previous = new TestAnalyzer(AnalyzerId);
        var replacement = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(previous);
        SubscribeToRegistrations();

        var replaced = await _registry.ReplaceAnalyzerAsync(replacement);

        Assert.That(replaced, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement));
        Assert.That(_told, Is.EqualTo(ToldOfTheAnalyzer));
    }

    [Test]
    public async Task UnregisterAnalyzerAsync_WhenASubscriberThrows_ReportsTheAnalyzerUnregisteredAndTellsTheOthers()
    {
        await _registry.RegisterAnalyzerAsync(new TestAnalyzer(AnalyzerId));
        SubscribeToUnregistrations();

        var unregistered = await _registry.UnregisterAnalyzerAsync(AnalyzerId);

        Assert.That(unregistered, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        Assert.That(_told, Is.EqualTo(ToldOfTheAnalyzer));
    }

    [Test]
    public async Task UnregisterAnalyzerInstanceAsync_WhenASubscriberThrows_ReportsTheAnalyzerUnregisteredAndTellsTheOthers()
    {
        var analyzer = new TestAnalyzer(AnalyzerId);
        await _registry.RegisterAnalyzerAsync(analyzer);
        SubscribeToUnregistrations();

        var unregistered = await _registry.UnregisterAnalyzerInstanceAsync(analyzer);

        Assert.That(unregistered, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
        Assert.That(_told, Is.EqualTo(ToldOfTheAnalyzer));
    }
}
