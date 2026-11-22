using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerServiceTests
{
    #region LoadAnalyzersAsync Tests

    [Test]
    public async Task LoadAnalyzersAsync_WithValidAssembly_ShouldLoadAndRegisterAnalyzers()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");
        var analyzers = ImmutableArray.Create(analyzer1, analyzer2);

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(analyzers);

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("analyzer1"));
        Assert.That(result[1].Id, Is.EqualTo("analyzer2"));

        await _mockLoader.Received(1).LoadAnalyzersFromAssemblyAsync(
            assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithFailure_ShouldReturnEmpty()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<IAnalyzer>.Empty);

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersAsync_ShouldStoreLoadedAnalyzers()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("test-analyzer");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        // Act
        await _service.LoadAnalyzersAsync(assemblyPath);
        var loadedAnalyzers = _service.GetLoadedAnalyzers();

        // Assert
        Assert.That(loadedAnalyzers.Length, Is.EqualTo(1));
        Assert.That(loadedAnalyzers[0].Id, Is.EqualTo("test-analyzer"));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithCancellation_ShouldReturnEmpty()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns<ImmutableArray<IAnalyzer>>(callInfo => throw new OperationCanceledException());

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath, cts.Token);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithRegistry_RegistersTheAnalyzersSoTheHostCanRunThem()
    {
        // Arrange
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        // Act
        await service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(registry.GetAnalyzer("analyzer1"), Is.SameAs(analyzer));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithRegistry_ReplacesAnAnalyzerRegisteredUnderTheSameId()
    {
        // Arrange - e.g. an auto-loaded analyzer with the ID of the one loaded explicitly
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var earlier = CreateMockAnalyzer("analyzer1");
        await registry.RegisterAnalyzerAsync(earlier);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        // Act
        await service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(registry.GetAnalyzer("analyzer1"), Is.SameAs(analyzer));
        Assert.That(service.GetAnalyzer("analyzer1"), Is.SameAs(analyzer));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WhenAnAnalyzerFailsToRegister_KeepsTheAnalyzerItWasToReplace()
    {
        // Arrange - the second analyzer with the ID fails to initialize, so the registry can't take it
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var earlierPath = CreateTestFile("earlier assembly", ".dll");
        var earlier = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(earlierPath, Arg.Any<CancellationToken>()).Returns(ImmutableArray.Create(earlier));
        await service.LoadAnalyzersAsync(earlierPath);
        var failingPath = CreateTestFile("failing assembly", ".dll");
        var failing = CreateMockAnalyzer("analyzer1");
        failing.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Cannot initialize")));
        _mockLoader.LoadAnalyzersFromAssemblyAsync(failingPath, Arg.Any<CancellationToken>()).Returns(ImmutableArray.Create(failing));

        // Act
        var loaded = await service.LoadAnalyzersAsync(failingPath);

        // Assert
        Assert.That(loaded, Is.Empty);
        Assert.That(registry.GetAnalyzer("analyzer1"), Is.SameAs(earlier));
        Assert.That(service.GetAnalyzer("analyzer1"), Is.SameAs(earlier));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WhenTheAnalyzerIsUnregisteredBeforeItIsPublished_DoesNotListIt()
    {
        // Arrange - the ID is unloaded as soon as the registry reports the analyzer registered
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));
        Task<bool>? unregistering = null;
        registry.AnalyzerRegistered += (_, args) => unregistering = registry.UnregisterAnalyzerAsync(args.AnalyzerId);

        // Act
        var loaded = await service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(unregistering, Is.Not.Null);
        Assert.That(await unregistering!, Is.True);
        Assert.That(loaded, Is.Empty);
        Assert.That(service.GetAnalyzer("analyzer1"), Is.Null, "an analyzer the registry no longer holds isn't listed as loaded");
    }

    [Test]
    public async Task LoadAnalyzersAsync_CancelledPartWay_ListsTheAnalyzersItHadRegistered()
    {
        // Arrange - the load's first analyzer replaces a listed one with its ID, and the load is cancelled while its
        // second analyzer initializes
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var earlierPath = CreateTestFile("earlier assembly", ".dll");
        var earlier = new TestAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(earlierPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create<IAnalyzer>(earlier));
        await service.LoadAnalyzersAsync(earlierPath);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var registered = new TestAnalyzer("analyzer1");
        var held = new TestAnalyzer("analyzer2", holdInitialization: true);
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create<IAnalyzer>(registered, held));
        using var cancellation = new CancellationTokenSource();

        // Act
        var loading = service.LoadAnalyzersAsync(assemblyPath, cancellation.Token);
        await held.WaitUntilInitializingAsync();
        await cancellation.CancelAsync();
        await loading;

        // Assert
        Assert.That(registry.GetLoadedAnalyzers(), Is.EqualTo(new IAnalyzer[] { registered }));
        Assert.That(service.GetAnalyzer("analyzer1"), Is.SameAs(registered), "the service lists the analyzer the registry holds");
        Assert.That(service.GetLoadedAnalyzers(), Is.EqualTo(new IAnalyzer[] { registered }));
        await held.WaitUntilDisposedAsync();
    }

    [Test]
    public async Task LoadAnalyzersAsync_WhenTheAnalyzerIsUnregisteredLater_StopsListingIt()
    {
        // Arrange - e.g. the analyzer host unloads the ID of one of the analyzers loaded
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var unloaded = CreateMockAnalyzer("analyzer1");
        var kept = CreateMockAnalyzer("analyzer2");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(unloaded, kept));
        await service.LoadAnalyzersAsync(assemblyPath);

        // Act
        var unregistered = await registry.UnregisterAnalyzerAsync("analyzer1");

        // Assert
        Assert.That(unregistered, Is.True);
        Assert.That(service.GetAnalyzer("analyzer1"), Is.Null, "an analyzer the registry no longer holds isn't listed as loaded");
        Assert.That(service.GetLoadedAnalyzers(), Is.EqualTo(new[] { kept }));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WhenAnotherLoadReplacesTheAnalyzerBeforeItIsPublished_ListsTheOneTheRegistryHolds()
    {
        // Arrange - two loads of one ID at once: the second registers and publishes its analyzer after the first has
        // registered its own, and before the first publishes it
        var registry = new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>());
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, registry: registry);
        var firstPath = CreateTestFile("first assembly", ".dll");
        var first = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(firstPath, Arg.Any<CancellationToken>()).Returns(ImmutableArray.Create(first));
        var secondPath = CreateTestFile("second assembly", ".dll");
        var second = CreateMockAnalyzer("analyzer1");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(secondPath, Arg.Any<CancellationToken>()).Returns(ImmutableArray.Create(second));
        Task<ImmutableArray<IAnalyzer>>? secondLoad = null;
        registry.AnalyzerRegistered += (_, _) =>
        {
            if (ReferenceEquals(registry.GetAnalyzer("analyzer1"), first))
            {
                secondLoad = service.LoadAnalyzersAsync(secondPath);
            }
        };

        // Act
        var firstLoaded = await service.LoadAnalyzersAsync(firstPath);

        // Assert
        Assert.That(secondLoad, Is.Not.Null);
        Assert.That(secondLoad!.IsCompletedSuccessfully, Is.True, "the second load finished before the first published");
        Assert.That(await secondLoad, Is.EqualTo(new[] { second }));
        Assert.That(registry.GetAnalyzer("analyzer1"), Is.SameAs(second));
        Assert.That(service.GetAnalyzer("analyzer1"), Is.SameAs(second), "the service lists the analyzer the registry holds");
        Assert.That(firstLoaded, Is.Empty, "the first load's analyzer was replaced before it could be listed");
    }

    #endregion

    #region LoadAnalyzersFromDirectoryAsync Tests

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithValidDirectory_ShouldLoadAllAnalyzers()
    {
        // Arrange
        var directory = CreateTestDirectory("analyzers");
        var assembly1 = Path.Combine(directory, "analyzer1.dll");
        var assembly2 = Path.Combine(directory, "analyzer2.dll");
        File.WriteAllText(assembly1, "test");
        File.WriteAllText(assembly2, "test");

        var assemblies = ImmutableArray.Create(assembly1, assembly2);
        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(assemblies);

        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assembly1, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1));
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assembly2, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer2));

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(2));
        _mockLoader.Received(1).DiscoverAnalyzerAssemblies(directory, true);
    }

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithEmptyDirectory_ShouldReturnEmpty()
    {
        // Arrange
        var directory = CreateTestDirectory("empty");

        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(ImmutableArray<string>.Empty);

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithError_ShouldReturnEmpty()
    {
        // Arrange
        var directory = CreateTestDirectory("error");

        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(x => throw new UnauthorizedAccessException());

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    #endregion
}
