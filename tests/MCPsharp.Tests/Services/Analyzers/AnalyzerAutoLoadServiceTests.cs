using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
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
public partial class AnalyzerAutoLoadServiceTests : FileServiceTestBase
{
    private static readonly string StaticMethodAnalyzerId = RoslynAnalyzerAdapter.GetId(typeof(StaticMethodAnalyzer));
    private static readonly string[] StaticMethodAnalyzerOnly = [StaticMethodAnalyzerId];
    private static readonly string[] BuiltInProvidersOnly = [AnalyzerAutoLoadConfiguration.BuiltInProvidersSource];
    private static readonly string[] CustomAnalyzerPath = ["/path/to/analyzers"];

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

    private AnalyzerAutoLoadService CreateService(AnalyzerAutoLoadConfiguration? configuration = null, RoslynAnalyzerLoader? loader = null) =>
        new(CreateNullLogger<AnalyzerAutoLoadService>(),
            loader ?? new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory()),
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
    public async Task EnsureAnalyzersLoadedAsync_WithAnAnalyzerTheRegistryRejects_ReportsItInsteadOfLoadingIt()
    {
        // e.g. an analyzer loaded with load_roslyn_analyzers already holds the ID
        var holder = new RoslynAnalyzerAdapter(new StaticMethodAnalyzer(), NullLogger<RoslynAnalyzerAdapter>.Instance);
        Assert.That(await _registry.RegisterAnalyzerAsync(holder), Is.True);
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Except(StaticMethodAnalyzerOnly)));
        Assert.That(result.Errors, Has.One.Contain(StaticMethodAnalyzerId).And.Contain("already registered"));
        Assert.That(_registry.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(holder));
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
    public async Task EnsureAnalyzersLoadedAsync_WithAnalyzerExcludedWithoutItsVersion_DoesNotLoadIt()
    {
        // An exclusion without the assembly version still applies after the analyzer package is upgraded
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());
        using var service = CreateService(new AnalyzerAutoLoadConfiguration
        {
            ExcludeAnalyzers = [RoslynAnalyzerAdapter.GetVersionIndependentId(typeof(StaticMethodAnalyzer))]
        });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers.Select(a => a.Id),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Where(id => id != StaticMethodAnalyzerId)));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithTwoBuildsOfAnAnalyzer_LoadsBoth()
    {
        // e.g. projects that take the code style analyzers from the SDK and from a NuGet package
        var assemblyName = $"ProbeBuilds_{Guid.NewGuid():N}";
        ProjectsReference(EmitProbeAnalyzer(assemblyName, "1.0.0.0"), EmitProbeAnalyzer(assemblyName, "2.0.0.0"));

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Name), Is.All.EqualTo("ProbeAnalyzer"));
        var ids = result.LoadedAnalyzers.Select(a => a.Id).ToList();
        Assert.That(ids, Has.Count.EqualTo(2));
        Assert.That(ids, Is.Unique);
        Assert.That(RegisteredIds(), Is.EquivalentTo(ids));
    }

    /// <summary>
    /// A reference to a build of ProbeAnalyzer in <paramref name="assemblyName"/>, in a directory of its own
    /// </summary>
    private AnalyzerFileReference EmitProbeAnalyzer(string assemblyName, string assemblyVersion)
    {
        var path = Path.Combine(CreateTestDirectory(Guid.NewGuid().ToString("N")), assemblyName + ".dll");
        AnalyzerAssemblyEmitter.Emit(assemblyName, path, AnalyzerAssemblyEmitter.AnalyzerSource("ProbeAnalyzer", "PROBE003"), assemblyVersion);
        return TestAnalyzerReferences.Create(path);
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithCustomPathDirectory_LoadsItsAnalyzers()
    {
        ProjectsReference();
        var directory = CreateTestDirectory("custom");
        File.Copy(typeof(StaticMethodAnalyzer).Assembly.Location, Path.Combine(directory, "MCPsharp.Analyzers.dll"));
        using var service = CreateService(new AnalyzerAutoLoadConfiguration { CustomAnalyzerPaths = [directory] });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithCustomPathOverTheScanLimit_ReportsTheLimitAndLoadsNothing()
    {
        ProjectsReference();
        var directory = CreateTestDirectory("custom-large");
        AnalyzerDirectoryScanLimitsTests.CreateFakeDlls(directory, AnalyzerDirectoryScanLimits.MaxAssemblyCount);
        File.Copy(typeof(StaticMethodAnalyzer).Assembly.Location, Path.Combine(directory, "MCPsharp.Analyzers.dll"));
        using var service = CreateService(new AnalyzerAutoLoadConfiguration { CustomAnalyzerPaths = [directory] });

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(result.Errors, Has.Some.Contain($"more than {AnalyzerDirectoryScanLimits.MaxAssemblyCount} DLLs"));
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
    public void LoadConfiguration_ReadsTheDocumentedFormat()
    {
        // The format of docs/analyzer-autoload-config.example.json
        var path = CreateTestFile($$"""
            {
              "$schema": "analyzer-autoload-schema.json",
              "autoLoadEnabled": false,
              "analyzerSources": ["builtin_providers"],
              "excludeAnalyzers": ["{{StaticMethodAnalyzerId}}"],
              "customAnalyzerPaths": ["/path/to/analyzers"]
            }
            """, ".json");

        var configuration = AnalyzerAutoLoadService.LoadConfiguration(path);

        Assert.That(configuration.AutoLoadEnabled, Is.False);
        Assert.That(configuration.AnalyzerSources, Is.EqualTo(BuiltInProvidersOnly));
        Assert.That(configuration.ExcludeAnalyzers, Is.EqualTo(StaticMethodAnalyzerOnly));
        Assert.That(configuration.CustomAnalyzerPaths, Is.EqualTo(CustomAnalyzerPath));
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
