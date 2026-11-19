using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// The auto-load loads the analyzers that the workspace's projects reference and nothing else (in particular nothing
/// from the NuGet cache). The workspace's references are the build's own analyzer assemblies.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerAutoLoadServiceTests : TestBase
{
    private const string StaticMethodAnalyzerId = "Roslyn_StaticMethodAnalyzer";

    private RoslynWorkspace _workspace = null!;
    private AnalyzerRegistry _registry = null!;
    private AnalyzerAutoLoadService _service = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _workspace = Substitute.For<RoslynWorkspace>();
        _registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        _service = CreateService();
    }

    [TearDown]
    protected override void TearDown()
    {
        _service.Dispose();
        _workspace.Dispose();
        base.TearDown();
    }

    private AnalyzerAutoLoadService CreateService(AnalyzerAutoLoadConfiguration? configuration = null) =>
        new(CreateNullLogger<AnalyzerAutoLoadService>(),
            new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory()),
            _registry,
            _workspace,
            configuration);

    private void ProjectsReference(params AnalyzerReference[] references) =>
        _workspace.GetAnalyzerReferences().Returns(references);

    private IEnumerable<string> RegisteredIds() => _registry.GetLoadedAnalyzers().Select(a => a.Id);

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_LoadsAndRegistersTheAnalyzersTheProjectsReference()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers(), TestAnalyzerReferences.BuiltInCodeFixes());

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.AlreadyLoaded, Is.False);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(RegisteredIds(), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithoutProjectReferences_LoadsNothing()
    {
        // No project open, or one that references no analyzers: nothing is loaded from anywhere else
        ProjectsReference();

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(RegisteredIds(), Is.Empty);
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithUnchangedReferences_IsAlreadyLoaded()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        var first = await _service.EnsureAnalyzersLoadedAsync();

        var second = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(second.AlreadyLoaded, Is.True);
        Assert.That(second.LoadedAnalyzers, Is.EqualTo(first.LoadedAnalyzers));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithChangedReferences_ReplacesTheAnalyzers()
    {
        // e.g. another project, which references no analyzers, was opened
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        await _service.EnsureAnalyzersLoadedAsync();

        ProjectsReference(TestAnalyzerReferences.BuiltInCodeFixes());
        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.AlreadyLoaded, Is.False);
        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(RegisteredIds(), Is.Empty, "the previous project's analyzers are unregistered");
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithChangedReferences_KeepsAnalyzersRegisteredElsewhere()
    {
        var other = new RoslynAnalyzerAdapter(new MultiDeclaratorTestAnalyzer(), NullLogger<RoslynAnalyzerAdapter>.Instance);
        await _registry.RegisterAnalyzerAsync(other);
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        await _service.EnsureAnalyzersLoadedAsync();

        ProjectsReference();
        await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(_registry.GetLoadedAnalyzers(), Is.EqualTo(new[] { other }));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithSameAnalyzersInTwoReferences_LoadsOneAnalyzerPerId()
    {
        // e.g. the SDK's and a NuGet package's build of the same analyzers
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers(), TestAnalyzerReferences.BuiltInAnalyzers());

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithExcludedAnalyzer_DoesNotLoadIt()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        using var service = CreateService(new AnalyzerAutoLoadConfiguration { ExcludeAnalyzers = [StaticMethodAnalyzerId] });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers.Select(a => a.Id),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Where(id => id != StaticMethodAnalyzerId)));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithoutProjectReferencesSource_DoesNotLoadTheProjectsAnalyzers()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        using var service = CreateService(new AnalyzerAutoLoadConfiguration
        {
            AnalyzerSources = [AnalyzerAutoLoadConfiguration.BuiltInProvidersSource]
        });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.Empty);
        _workspace.DidNotReceive().GetAnalyzerReferences();
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAutoLoadDisabled_LoadsNothing()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        using var service = CreateService(new AnalyzerAutoLoadConfiguration { AutoLoadEnabled = false });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(RegisteredIds(), Is.Empty);
    }

    [Test]
    public async Task Reset_MakesTheNextCallLoadAgain()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        await _service.EnsureAnalyzersLoadedAsync();

        _service.Reset();
        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.AlreadyLoaded, Is.False);
        Assert.That(result.Errors, Is.Empty, "the previous registrations are replaced, not duplicated");
        Assert.That(RegisteredIds(), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task GetStatus_ReportsTheAutoLoadedAnalyzers()
    {
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        Assert.That(_service.GetStatus().HasAutoLoaded, Is.False);

        await _service.EnsureAnalyzersLoadedAsync();

        var status = _service.GetStatus();
        Assert.That(status.HasAutoLoaded, Is.True);
        Assert.That(status.AutoLoadedCount, Is.EqualTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Length));
    }

    [Test]
    public void DefaultConfiguration_LoadsTheProjectsAnalyzerReferences()
    {
        Assert.That(AnalyzerAutoLoadConfiguration.Default.AnalyzerSources,
            Does.Contain(AnalyzerAutoLoadConfiguration.ProjectReferencesSource));
        Assert.That(new AnalyzerAutoLoadConfiguration().AnalyzerSources,
            Is.EqualTo(AnalyzerAutoLoadConfiguration.Default.AnalyzerSources));
    }
}
