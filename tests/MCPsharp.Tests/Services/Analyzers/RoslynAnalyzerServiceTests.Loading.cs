using System.Collections.Immutable;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.Analyzers;

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
