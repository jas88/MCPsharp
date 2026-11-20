using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// AnalyzerRegistry replaces an analyzer, and unregisters a particular one, in one step
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

    private static IAnalyzer CreateAnalyzer(Task? initialization = null)
    {
        var analyzer = Substitute.For<IAnalyzer>();
        analyzer.Id.Returns(AnalyzerId);
        analyzer.Name.Returns("Test analyzer");
        analyzer.Version.Returns(new Version(1, 0, 0));
        analyzer.SupportedExtensions.Returns(ImmutableArray.Create(".cs"));
        analyzer.GetRules().Returns(ImmutableArray<AnalyzerRule>.Empty);
        analyzer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(initialization ?? Task.CompletedTask);
        return analyzer;
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_RegistersTheAnalyzerInPlaceOfTheOneWithItsId()
    {
        var previous = CreateAnalyzer();
        var replacement = CreateAnalyzer();
        await _registry.RegisterAnalyzerAsync(previous);

        var replaced = await _registry.ReplaceAnalyzerAsync(replacement);

        Assert.That(replaced, Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(replacement));
        Assert.That(_registry.GetRegisteredAnalyzers(), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task ReplaceAnalyzerAsync_WithAnAnalyzerThatFailsToInitialize_KeepsTheOneItWasToReplace()
    {
        var previous = CreateAnalyzer();
        var failing = CreateAnalyzer(Task.FromException(new InvalidOperationException("Cannot initialize")));
        await _registry.RegisterAnalyzerAsync(previous);

        var replaced = await _registry.ReplaceAnalyzerAsync(failing);

        Assert.That(replaced, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(previous));
        await previous.DidNotReceive().DisposeAsync();
    }

    [Test]
    public async Task UnregisterAnalyzerInstanceAsync_LeavesAnAnalyzerThatHasReplacedIt()
    {
        // e.g. the auto-load unregistering its analyzer after load_roslyn_analyzers has replaced it
        var autoLoaded = CreateAnalyzer();
        var explicitlyLoaded = CreateAnalyzer();
        await _registry.RegisterAnalyzerAsync(autoLoaded);
        await _registry.ReplaceAnalyzerAsync(explicitlyLoaded);

        var unregistered = await _registry.UnregisterAnalyzerInstanceAsync(autoLoaded);

        Assert.That(unregistered, Is.False);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.SameAs(explicitlyLoaded));
        Assert.That(await _registry.UnregisterAnalyzerInstanceAsync(explicitlyLoaded), Is.True);
        Assert.That(_registry.GetAnalyzer(AnalyzerId), Is.Null);
    }
}
