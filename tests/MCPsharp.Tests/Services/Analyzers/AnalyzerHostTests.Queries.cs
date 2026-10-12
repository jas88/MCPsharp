using System.Collections.Immutable;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class AnalyzerHostTests
{
    [Test]
    public async Task GetLoadedAnalyzersAsync_ShouldReturnLoadedAnalyzers()
    {
        // Arrange
        var expectedAnalyzers = new List<IAnalyzer>
        {
            CreateMockAnalyzer("analyzer-1", "Analyzer 1", "1.0.0"),
            CreateMockAnalyzer("analyzer-2", "Analyzer 2", "2.0.0")
        };

        _mockAnalyzerRegistry.GetLoadedAnalyzers().Returns(expectedAnalyzers);

        // Act
        var result = await _analyzerHost.GetLoadedAnalyzersAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Any(a => a.Id == "analyzer-1"), Is.True);
        Assert.That(result.Any(a => a.Id == "analyzer-2"), Is.True);

        _mockAnalyzerRegistry.Received(1).GetLoadedAnalyzers();
    }

    [Test]
    public async Task GetAnalyzerCapabilitiesAsync_ShouldReturnCapabilities()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = Substitute.For<IAnalyzer>();
        var expectedCapabilities = new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#", "VB.NET" },
            SupportedFileTypes = new[] { ".cs", ".vb" },
            MaxFileSize = 10 * 1024 * 1024,
            CanAnalyzeProjects = true,
            CanAnalyzeSolutions = true
        };

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.GetCapabilities().Returns(expectedCapabilities);

        // Act
        var result = await _analyzerHost.GetAnalyzerCapabilitiesAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.SupportedLanguages, Is.EqualTo(expectedCapabilities.SupportedLanguages));
        Assert.That(result.SupportedFileTypes, Is.EqualTo(expectedCapabilities.SupportedFileTypes));
        Assert.That(result.MaxFileSize, Is.EqualTo(expectedCapabilities.MaxFileSize));
    }

    [Test]
    public async Task GetAnalyzerCapabilitiesAsync_WithNonLoadedAnalyzer_ShouldFail()
    {
        // Arrange
        var analyzerId = "non-loaded-analyzer";

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

        // Act
        var result = await _analyzerHost.GetAnalyzerCapabilitiesAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLoadedAnalyzers_ShouldReturnLoadedAnalyzers()
    {
        // Arrange
        var expectedAnalyzerInfos = new[]
        {
            new AnalyzerInfo
            {
                Id = "analyzer-1",
                Name = "Analyzer 1",
                Version = new Version("1.0.0"),
                Description = "Description for Analyzer 1",
                Author = "Test Author",
                SupportedExtensions = ImmutableArray.Create(".cs"),
                IsEnabled = true
            },
            new AnalyzerInfo
            {
                Id = "analyzer-2",
                Name = "Analyzer 2",
                Version = new Version("2.0.0"),
                Description = "Description for Analyzer 2",
                Author = "Test Author",
                SupportedExtensions = ImmutableArray.Create(".cs"),
                IsEnabled = true
            }
        };

        _mockAnalyzerRegistry.GetRegisteredAnalyzers().Returns(expectedAnalyzerInfos.ToImmutableArray());

        // Act
        var result = _analyzerHost.GetLoadedAnalyzers();

        // Assert
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result.Any(ai => ai.Id == "analyzer-1"), Is.True);
        Assert.That(result.Any(ai => ai.Id == "analyzer-2"), Is.True);

        _mockAnalyzerRegistry.Received(1).GetRegisteredAnalyzers();
    }

    [Test]
    public async Task GetHealthStatusAsync_ShouldReturnHealthStatus()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = await _analyzerHost.GetHealthStatusAsync();

        // Assert
        Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetAnalyzer_WithValidId_ShouldReturnAnalyzer()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = _analyzerHost.GetAnalyzer(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.Id, Is.EqualTo(analyzerId));

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
    }

    [Test]
    public void GetAnalyzer_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var analyzerId = "non-existent-analyzer";

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

        // Act
        var result = _analyzerHost.GetAnalyzer(analyzerId);

        // Assert
        Assert.That(result, Is.Null);

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
    }
}
