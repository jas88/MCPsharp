using MCPsharp.Models;
using MCPsharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Cross-file checks of <see cref="ConfigAnalyzerService.ValidateConfigurationConsistencyAsync"/>.
/// </summary>
[TestFixture]
public class ConfigAnalyzerServiceTests
{
    private const string CrossFileConflict = "Configuration conflict for key 'Mode' between files";
    private const string EnvironmentConflict = "Environment 'development' has conflicting configuration values";

    private string _projectRoot = null!;
    private ConfigAnalyzerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp_config_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
        _service = new ConfigAnalyzerService(NullLogger<ConfigAnalyzerService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectRoot))
        {
            Directory.Delete(_projectRoot, true);
        }
    }

    [Test]
    public async Task ValidateConfigurationConsistencyAsync_ConflictingFiles_ReportsEachConflict()
    {
        WriteConfig("appsettings.json", "first");
        WriteConfig("appsettings.development.json", "second");
        WriteConfig("other.development.json", "third");

        var result = await _service.ValidateConfigurationConsistencyAsync(_projectRoot);

        var crossFile = result.Issues.Where(i => i.Message == CrossFileConflict).ToList();
        var environment = result.Issues.Where(i => i.Message == EnvironmentConflict).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(crossFile, Has.Count.EqualTo(2), "each later file disagrees with the one before it");
            Assert.That(crossFile.Select(i => i.Severity), Is.All.EqualTo(ValidationSeverity.Error));
            Assert.That(environment, Has.Count.EqualTo(1), "the two development files disagree");
            Assert.That(environment.Select(i => i.Severity), Is.All.EqualTo(ValidationSeverity.Warning));
            Assert.That(result.IsValid, Is.False);
        });
    }

    [Test]
    public async Task ValidateConfigurationConsistencyAsync_AgreeingFiles_ReportsNoConflict()
    {
        WriteConfig("appsettings.json", "same");
        WriteConfig("appsettings.development.json", "same");
        WriteConfig("other.development.json", "same");

        var result = await _service.ValidateConfigurationConsistencyAsync(_projectRoot);

        Assert.That(result.Issues.Select(i => i.Message),
            Has.None.EqualTo(CrossFileConflict).And.None.EqualTo(EnvironmentConflict));
    }

    private void WriteConfig(string fileName, string mode) =>
        File.WriteAllText(Path.Combine(_projectRoot, fileName), $$"""{ "Mode": "{{mode}}" }""");
}
