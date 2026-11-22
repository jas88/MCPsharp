using System.Collections.Immutable;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.Diagnostics;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerServiceTests
{
    private static readonly string[] ExplicitAnalyzerId = ["explicit"];

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_LoadsTheAnalyzersTheOpenProjectReferences()
    {
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_AfterTheProjectChanged_ReplacesAutoLoadedAnalyzersButKeepsExplicitOnes()
    {
        using var workspace = Substitute.For<RoslynWorkspace>();
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var explicitAnalyzer = CreateMockAnalyzer(ExplicitAnalyzerId[0]);
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(explicitAnalyzer));
        await service.LoadAnalyzersAsync(assemblyPath);

        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        await service.EnsureAnalyzersLoadedAsync();
        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Concat(ExplicitAnalyzerId)));

        // Another project, which references no analyzers, is opened
        workspace.GetAnalyzerReferences().Returns(Array.Empty<AnalyzerReference>());
        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EqualTo(ExplicitAnalyzerId));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithTheIdOfAnAutoLoadedAnalyzer_ReplacesItAndKeepsTheIdThroughTheNextAutoLoad()
    {
        // The precedence the auto-load documentation gives load_roslyn_analyzers over the auto-loaded analyzers
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        using var autoLoad = CreateAutoLoadService(workspace, registry);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad, registry);
        await service.EnsureAnalyzersLoadedAsync();
        var autoLoaded = registry.GetAnalyzer(StaticMethodAnalyzerId);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var explicitlyLoaded = CreateMockAnalyzer(StaticMethodAnalyzerId);
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(explicitlyLoaded));

        await service.LoadAnalyzersAsync(assemblyPath);

        Assert.That(autoLoaded, Is.Not.Null.And.Not.SameAs(explicitlyLoaded));
        Assert.That(registry.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(explicitlyLoaded));
        Assert.That(service.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(explicitlyLoaded));

        // The project's analyzers are auto-loaded again, one of them with the ID the explicitly loaded analyzer holds
        autoLoad.Reset();
        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(registry.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(explicitlyLoaded));
        Assert.That(service.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(explicitlyLoaded));
        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(registry.GetLoadedAnalyzers(), Is.EquivalentTo(service.GetLoadedAnalyzers()), "the service lists the analyzers the registry holds");
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenAnAutoLoadedAnalyzerIsUnregisteredBeforeItIsPublished_DoesNotListIt()
    {
        // One analyzer's ID is unloaded as soon as the registry reports the analyzer registered
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        using var autoLoad = CreateAutoLoadService(workspace, registry);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad, registry);
        Task<bool>? unregistering = null;
        registry.AnalyzerRegistered += (_, args) =>
        {
            if (args.AnalyzerId == StaticMethodAnalyzerId)
            {
                unregistering = registry.UnregisterAnalyzerAsync(args.AnalyzerId);
            }
        };

        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(unregistering, Is.Not.Null);
        Assert.That(await unregistering!, Is.True);
        var remaining = TestAnalyzerReferences.BuiltInAnalyzerIds.Where(id => id != StaticMethodAnalyzerId).ToList();
        Assert.That(registry.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(remaining));
        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(remaining), "the service lists the analyzers the registry holds");
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenALoadIsCancelled_StopsListingTheAnalyzersItUnregistered()
    {
        // A load starts by unregistering the analyzers of the load before it, and leaves none registered if it is
        // cancelled after that
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        using var autoLoad = CreateAutoLoadService(workspace, registry);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad, registry);
        await service.EnsureAnalyzersLoadedAsync();
        var listedBefore = service.GetLoadedAnalyzers().Length;

        // Another project is opened, and the load of its analyzers is cancelled once it has unregistered the others
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInCodeFixes() });
        using var cancellation = new CancellationTokenSource();
        registry.AnalyzerUnregistered += (_, _) => cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => service.EnsureAnalyzersLoadedAsync(cancellation.Token));

        Assert.That(listedBefore, Is.EqualTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Length));
        Assert.That(registry.GetLoadedAnalyzers(), Is.Empty);
        Assert.That(service.GetLoadedAnalyzers(), Is.Empty, "analyzers the registry no longer holds aren't listed as loaded");
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_BeforeAnUnregistrationIsHandled_ListsTheAnalyzerRegisteredUnderTheFreedId()
    {
        // An explicitly loaded analyzer's ID is unloaded, and the project's analyzers, one of which has that ID, are
        // auto-loaded before the service is told of the unregistration: the subscriber here is told first
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        RoslynAnalyzerService? service = null;
        Task? autoLoading = null;
        registry.AnalyzerUnregistered += (_, _) => autoLoading ??= service!.EnsureAnalyzersLoadedAsync();
        using var autoLoad = CreateAutoLoadService(workspace, registry);
        service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad, registry);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var explicitlyLoaded = CreateMockAnalyzer(StaticMethodAnalyzerId);
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(explicitlyLoaded));
        await service.LoadAnalyzersAsync(assemblyPath);

        var unregistered = await registry.UnregisterAnalyzerAsync(StaticMethodAnalyzerId);

        Assert.That(unregistered, Is.True);
        Assert.That(autoLoading, Is.Not.Null);
        await autoLoading!;
        var registered = registry.GetAnalyzer(StaticMethodAnalyzerId);
        Assert.That(registered, Is.Not.Null.And.Not.SameAs(explicitlyLoaded));
        Assert.That(service.GetAnalyzer(StaticMethodAnalyzerId), Is.SameAs(registered));
        Assert.That(service.GetLoadedAnalyzers(), Is.EquivalentTo(registry.GetLoadedAnalyzers()), "the service lists the analyzers the registry holds");
    }

    [Test]
    public async Task RunAnalyzersAsync_AfterWaitingForAnotherCallersAutoLoad_RunsTheAnalyzersOfThatLoad()
    {
        // e.g. a request that arrives while the analyzers load at startup: it waits for that load, and by the time
        // it goes on, the load's analyzers are listed
        var file = CreateTestFile("public class Calc { }", ".cs");
        var reference = TestAnalyzerReferences.BuiltInAnalyzers();
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { reference });
        workspace.GetFileAnalyzerScope(Arg.Any<string>()).Returns(new FileAnalyzerScope(1, [reference]));
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        using var autoLoad = CreateAutoLoadService(workspace, registry);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad, registry);
        HostReports((_, _) => []);
        Task<AnalyzerRunResult>? waitingRun = null;
        registry.AnalyzerRegistered += (_, _) => waitingRun ??= service.RunAnalyzersAsync(file);

        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(waitingRun, Is.Not.Null);
        var run = await waitingRun!;
        Assert.That(run.Success, Is.True, run.ErrorMessage);
        Assert.That(run.AnalyzersRun, Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task LoadOnStartupAsync_WhenTheProjectConfigurationSetsLoadOnStartup_LoadsTheAnalyzersNow()
    {
        using var workspace = OpenProjectReferencingTheBuiltInAnalyzers("""{ "loadOnStartup": true }""");
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        await service.LoadOnStartupAsync();

        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task LoadOnStartupAsync_ByDefault_LeavesLoadingToFirstUse()
    {
        using var workspace = OpenProjectReferencingTheBuiltInAnalyzers(configurationJson: null);
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        await service.LoadOnStartupAsync();

        Assert.That(service.GetLoadedAnalyzers(), Is.Empty);
        workspace.DidNotReceive().GetAnalyzerReferences();
    }

    /// <summary>
    /// A workspace whose project, in <see cref="FileServiceTestBase.TempDirectory"/>, references the built-in
    /// analyzers and has <paramref name="configurationJson"/> as its auto-load configuration (none when null)
    /// </summary>
    private RoslynWorkspace OpenProjectReferencingTheBuiltInAnalyzers(string? configurationJson)
    {
        var workspace = Substitute.For<RoslynWorkspace>();
        workspace.LoadedProjectDirectory.Returns(TempDirectory);
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        if (configurationJson != null)
        {
            var path = AnalyzerAutoLoadService.GetConfigurationPath(TempDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, configurationJson);
        }

        return workspace;
    }

    private static AnalyzerAutoLoadService CreateAutoLoadService(RoslynWorkspace workspace, AnalyzerRegistry? registry = null) =>
        new(CreateNullLogger<AnalyzerAutoLoadService>(),
            new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory()),
            registry ?? new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>()),
            workspace);
}
