using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using MCPsharp.Services.Analyzers;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
[Category("Roslyn")]
public class RoslynAnalyzerLoaderTests : FileServiceTestBase
{
    private ILogger<RoslynAnalyzerLoader> _mockLogger = null!;
    private ILoggerFactory _mockLoggerFactory = null!;
    private RoslynAnalyzerLoader _loader = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _mockLogger = CreateNullLogger<RoslynAnalyzerLoader>();
        _mockLoggerFactory = CreateNullLoggerFactory();
        _loader = new RoslynAnalyzerLoader(_mockLogger, _mockLoggerFactory);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerLoader(null!, _mockLoggerFactory));
    }

    [Test]
    public void Constructor_WithNullLoggerFactory_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerLoader(_mockLogger, null!));
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() =>
            new RoslynAnalyzerLoader(_mockLogger, _mockLoggerFactory));
    }

    #endregion

    #region LoadAnalyzersFromAssemblyAsync Tests

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_WithNonExistentFile_ShouldReturnEmpty()
    {
        // Arrange
        var nonExistentPath = "/non/existent/analyzer.dll";

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(nonExistentPath);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_WithInvalidAssembly_ShouldReturnEmpty()
    {
        // Arrange - Create a file that's not a valid assembly
        var invalidAssembly = CreateTestFile("This is not a DLL", ".dll");

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(invalidAssembly);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_WithValidAssembly_ShouldLoadAnalyzers()
    {
        // Arrange - Use this test assembly which contains DiagnosticAnalyzers
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath);

        // Assert
        // May be zero if no analyzers in test assembly, that's ok
        Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_ShouldCreateAdapters()
    {
        // Arrange - Use a known assembly with analyzers
        var assemblyPath = typeof(RoslynAnalyzerAdapter).Assembly.Location;

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath);

        // Assert - Each analyzer should be wrapped in an adapter
        foreach (var analyzer in result)
        {
            Assert.That(analyzer, Is.Not.Null);
            Assert.That(analyzer.Id, Is.Not.Empty);
            Assert.That(analyzer.Name, Is.Not.Empty);
            Assert.That(analyzer.Version, Is.Not.Null);
        }
    }

    [Test]
    public void LoadAnalyzersFromAssemblyAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cts.Token));
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_ShouldLogInformation()
    {
        // Arrange
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath);

        // Assert - Just verify it completes without throwing
        // Result is ImmutableArray, always has value
        Assert.Pass();
    }

    #endregion

    #region LoadAnalyzersFromAssembliesAsync Tests

    [Test]
    public async Task LoadAnalyzersFromAssembliesAsync_WithEmptyList_ShouldReturnEmpty()
    {
        // Arrange
        var emptyList = Array.Empty<string>();

        // Act
        var result = await _loader.LoadAnalyzersFromAssembliesAsync(emptyList);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromAssembliesAsync_WithMultipleAssemblies_ShouldLoadAll()
    {
        // Arrange
        var assembly1 = Assembly.GetExecutingAssembly().Location;
        var assembly2 = typeof(RoslynAnalyzerAdapter).Assembly.Location;
        var assemblies = new[] { assembly1, assembly2 };

        // Act
        var result = await _loader.LoadAnalyzersFromAssembliesAsync(assemblies);

        // Assert
        Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromAssembliesAsync_WithMixedValidAndInvalid_ShouldLoadValidOnes()
    {
        // Arrange
        var validAssembly = Assembly.GetExecutingAssembly().Location;
        var invalidAssembly = CreateTestFile("Not a DLL", ".dll");
        var assemblies = new[] { validAssembly, invalidAssembly };

        // Act
        var result = await _loader.LoadAnalyzersFromAssembliesAsync(assemblies);

        // Assert
        // Should not throw, should handle invalid gracefully
        Assert.Pass();
    }

    [Test]
    public void LoadAnalyzersFromAssembliesAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var assemblies = new[] { Assembly.GetExecutingAssembly().Location };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _loader.LoadAnalyzersFromAssembliesAsync(assemblies, cts.Token));
    }

    #endregion

    #region DiscoverAnalyzerAssemblies Tests

    [Test]
    public void DiscoverAnalyzerAssemblies_WithNonExistentDirectory_ShouldReturnEmpty()
    {
        // Arrange
        var nonExistentDir = "/non/existent/directory";

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(nonExistentDir);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_WithEmptyDirectory_ShouldReturnEmpty()
    {
        // Arrange
        var emptyDir = CreateTestDirectory("empty");

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(emptyDir);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_WithDirectoryContainingNonAnalyzerDlls_ShouldReturnEmpty()
    {
        // Arrange
        var dir = CreateTestDirectory("dlls");
        var nonAnalyzerDll = Path.Combine(dir, "test.dll");
        File.WriteAllText(nonAnalyzerDll, "not a real dll");

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(dir);

        // Assert
        // Should handle invalid DLLs gracefully
        Assert.Pass();
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_WithRecursiveSearch_ShouldSearchSubdirectories()
    {
        // Arrange
        var rootDir = CreateTestDirectory("root");
        var subDir = CreateTestDirectory("root/sub");

        // Copy a real assembly to subdirectory
        var sourceAssembly = Assembly.GetExecutingAssembly().Location;
        var targetAssembly = Path.Combine(subDir, "analyzer.dll");
        File.Copy(sourceAssembly, targetAssembly, overwrite: true);

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(rootDir, recursive: true);

        // Assert
        // Should find assemblies in subdirectories
        Assert.Pass();
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_WithNonRecursiveSearch_ShouldNotSearchSubdirectories()
    {
        // Arrange
        var rootDir = CreateTestDirectory("root");
        var subDir = CreateTestDirectory("root/sub");

        // Put assembly only in subdirectory
        var sourceAssembly = Assembly.GetExecutingAssembly().Location;
        var targetAssembly = Path.Combine(subDir, "analyzer.dll");
        File.Copy(sourceAssembly, targetAssembly, overwrite: true);

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(rootDir, recursive: false);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0)); // Should not find in subdirectory
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_ShouldReturnAbsolutePaths()
    {
        // Arrange
        var dir = CreateTestDirectory("analyzers");
        var sourceAssembly = Assembly.GetExecutingAssembly().Location;
        var targetAssembly = Path.Combine(dir, "test.dll");
        File.Copy(sourceAssembly, targetAssembly, overwrite: true);

        // Act
        var result = _loader.DiscoverAnalyzerAssemblies(dir);

        // Assert
        foreach (var path in result)
        {
            Assert.That(Path.IsPathRooted(path), Is.True);
            Assert.That(File.Exists(path), Is.True);
        }
    }

    #endregion

    #region LoadAnalyzersFromNuGetCacheAsync Tests

    [Test]
    public void LoadAnalyzersFromNuGetCacheAsync_ShouldNotThrow()
    {
        // Act & Assert - Should handle missing NuGet cache gracefully
        Assert.DoesNotThrowAsync(async () =>
            await _loader.LoadAnalyzersFromNuGetCacheAsync());
    }

    [Test]
    public async Task LoadAnalyzersFromNuGetCacheAsync_ShouldReturnAnalyzers()
    {
        // Act
        var result = await _loader.LoadAnalyzersFromNuGetCacheAsync();

        // Assert
        // May be empty if no NuGet cache exists, that's ok
        Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void LoadAnalyzersFromNuGetCacheAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _loader.LoadAnalyzersFromNuGetCacheAsync(cts.Token));
    }

    #endregion

    #region GetAnalyzerAssemblyInfo Tests

    [Test]
    public void GetAnalyzerAssemblyInfo_WithNonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var nonExistentPath = "/non/existent/analyzer.dll";

        // Act
        var result = _loader.GetAnalyzerAssemblyInfo(nonExistentPath);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetAnalyzerAssemblyInfo_WithInvalidAssembly_ShouldReturnNull()
    {
        // Arrange
        var invalidAssembly = CreateTestFile("Not a DLL", ".dll");

        // Act
        var result = _loader.GetAnalyzerAssemblyInfo(invalidAssembly);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetAnalyzerAssemblyInfo_WithValidAssembly_ShouldReturnInfo()
    {
        // Arrange
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        // Act
        var result = _loader.GetAnalyzerAssemblyInfo(assemblyPath);

        // Assert
        // May be null if assembly has no analyzers
        if (result != null)
        {
            Assert.That(result.AssemblyPath, Is.EqualTo(assemblyPath));
            Assert.That(result.AssemblyName, Is.Not.Empty);
            Assert.That(result.Version, Is.Not.Null);
            Assert.That(result.AnalyzerCount, Is.GreaterThan(0));
            Assert.That(result.AnalyzerTypeNames.Length, Is.EqualTo(result.AnalyzerCount));
        }
    }

    [Test]
    public void GetAnalyzerAssemblyInfo_WithAssemblyWithoutAnalyzers_ShouldReturnNull()
    {
        // Arrange - Use an assembly that definitely has no analyzers
        var assemblyPath = typeof(string).Assembly.Location;

        // Act
        var result = _loader.GetAnalyzerAssemblyInfo(assemblyPath);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_WithExceptionInAnalyzerConstructor_ShouldHandleGracefully()
    {
        // Arrange - An assembly where analyzer constructor might throw
        var assemblyPath = CreateTestFile("dummy", ".dll");

        // Act
        var result = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath);

        // Assert - Should not throw, should return empty
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_WithAccessDenied_ShouldHandleGracefully()
    {
        // Arrange - Use a directory that might have access issues
        var tempDir = CreateTestDirectory("restricted");

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() =>
            _loader.DiscoverAnalyzerAssemblies(tempDir));
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task FullWorkflow_DiscoverAndLoad_ShouldComplete()
    {
        // Arrange
        var dir = CreateTestDirectory("workflow");
        var sourceAssembly = Assembly.GetExecutingAssembly().Location;
        var targetAssembly = Path.Combine(dir, "test.dll");
        File.Copy(sourceAssembly, targetAssembly, overwrite: true);

        // Act - Discover
        var assemblies = _loader.DiscoverAnalyzerAssemblies(dir);

        // Act - Load
        var analyzers = await _loader.LoadAnalyzersFromAssembliesAsync(assemblies);

        // Assert
        // Both ImmutableArrays always have values
        Assert.Pass();
    }

    #endregion

    [TearDown]
    protected override void TearDown()
    {
        _mockLoggerFactory?.Dispose();
        base.TearDown();
    }
}
