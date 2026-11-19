using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// The open project's .mcpsharp/autoload-config.json configures the auto-load
/// </summary>
public partial class AnalyzerAutoLoadServiceTests
{
    /// <summary>
    /// Open a project in <see cref="FileServiceTestBase.TempDirectory"/> that references the built-in analyzers,
    /// with <paramref name="configurationJson"/> as its auto-load configuration (none when null)
    /// </summary>
    private string OpenProjectWithConfiguration(string? configurationJson)
    {
        _workspace.LoadedProjectDirectory.Returns(TempDirectory);
        ProjectsReference(TestAnalyzerReferences.BuiltInAnalyzers());

        var path = AnalyzerAutoLoadService.GetConfigurationPath(TempDirectory);
        if (configurationJson != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, configurationJson);
        }

        return path;
    }

    [Test]
    public void GetConfigurationPath_IsInTheProjectsMcpsharpDirectory()
    {
        Assert.That(AnalyzerAutoLoadService.GetConfigurationPath(TempDirectory),
            Is.EqualTo(Path.Combine(TempDirectory, ".mcpsharp", "autoload-config.json")));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_AppliesTheOpenProjectsConfiguration()
    {
        OpenProjectWithConfiguration($$"""{ "excludeAnalyzers": ["{{StaticMethodAnalyzerId}}"] }""");

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Where(id => id != StaticMethodAnalyzerId)));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenTheProjectsConfigurationDisablesAutoLoad_LoadsNothing()
    {
        OpenProjectWithConfiguration("""{ "autoLoadEnabled": false }""");

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(RegisteredIds(), Is.Empty);
        _workspace.DidNotReceive().GetAnalyzerReferences();
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithoutAConfigurationFile_UsesTheDefaultConfiguration()
    {
        OpenProjectWithConfiguration(null);

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(_service.GetConfiguration(), Is.SameAs(AnalyzerAutoLoadConfiguration.Default));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAMalformedConfigurationFile_UsesTheDefaultConfigurationAndReportsTheFile()
    {
        var path = OpenProjectWithConfiguration("""{ "autoLoadEnabled": false, """);

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds),
            "the malformed file's autoLoadEnabled: false is not applied");
        Assert.That(result.Errors, Has.Some.Contain(path).And.Contain("could not be read"));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WhenTheConfigurationFileChanges_LoadsAgainWithTheNewConfiguration()
    {
        var path = OpenProjectWithConfiguration($$"""{ "excludeAnalyzers": ["{{StaticMethodAnalyzerId}}"] }""");
        var first = await _service.EnsureAnalyzersLoadedAsync();

        await File.WriteAllTextAsync(path, """{ "excludeAnalyzers": [] }""");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
        var second = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(first.LoadedAnalyzers, Has.Length.EqualTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Length - 1));
        Assert.That(second.AlreadyLoaded, Is.False);
        Assert.That(second.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(RegisteredIds(), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAnUnchangedConfigurationFile_IsAlreadyLoaded()
    {
        OpenProjectWithConfiguration("""{ "excludeAnalyzers": [] }""");
        await _service.EnsureAnalyzersLoadedAsync();

        var second = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(second.AlreadyLoaded, Is.True);
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_ResolvesRelativeCustomAnalyzerPathsAgainstTheProject()
    {
        OpenProjectWithConfiguration("""{ "customAnalyzerPaths": ["tools/analyzers"] }""");
        ProjectsReference(); // the analyzers can only come from the custom path
        var analyzerDirectory = CreateTestDirectory(Path.Combine("tools", "analyzers"));
        File.Copy(typeof(StaticMethodAnalyzer).Assembly.Location, Path.Combine(analyzerDirectory, "MCPsharp.Analyzers.dll"));

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_WithAnUnknownAnalyzerSource_ReportsIt()
    {
        // e.g. nuget_cache, which earlier documentation listed
        OpenProjectWithConfiguration("""{ "analyzerSources": ["nuget_cache"] }""");

        var result = await _service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.LoadedAnalyzers, Is.Empty);
        Assert.That(result.Errors, Has.Some.Contain("'nuget_cache'").And.Contain(AnalyzerAutoLoadConfiguration.ProjectReferencesSource));
    }

    [Test]
    public void GetConfiguration_ReadsTheOpenProjectsLoadOnStartup()
    {
        OpenProjectWithConfiguration("""{ "loadOnStartup": true }""");

        Assert.That(_service.GetConfiguration().LoadOnStartup, Is.True);
    }

    [Test]
    public void GetConfiguration_WithoutAnOpenProject_IsTheDefaultConfiguration()
    {
        Assert.That(_service.GetConfiguration(), Is.SameAs(AnalyzerAutoLoadConfiguration.Default));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_OnARealWorkspace_ReadsTheConfigurationOfTheOpenedDirectory()
    {
        // A directory without a project file opens as an adhoc workspace, which has no analyzer references
        var projectDirectory = CreateTestDirectory("project");
        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Calc.cs"), RoslynCodeFixApplierTests.TwoStaticCandidatesSource);
        var analyzerDirectory = CreateTestDirectory(Path.Combine("project", "analyzers"));
        File.Copy(typeof(StaticMethodAnalyzer).Assembly.Location, Path.Combine(analyzerDirectory, "MCPsharp.Analyzers.dll"));
        var configurationPath = AnalyzerAutoLoadService.GetConfigurationPath(projectDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        await File.WriteAllTextAsync(configurationPath, """{ "customAnalyzerPaths": ["analyzers"] }""");

        using var workspace = new RoslynWorkspace();
        await workspace.InitializeAsync(projectDirectory);
        using var service = new AnalyzerAutoLoadService(
            NullLogger<AnalyzerAutoLoadService>.Instance,
            new RoslynAnalyzerLoader(NullLogger<RoslynAnalyzerLoader>.Instance, NullLoggerFactory.Instance),
            _registry,
            workspace);

        var result = await service.EnsureAnalyzersLoadedAsync();

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.LoadedAnalyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }
}
