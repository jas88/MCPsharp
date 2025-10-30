using MCPsharp.Services;
using Xunit;

namespace MCPsharp.Tests;

/// <summary>
/// Standalone tests for WorkflowAnalyzerService that don't depend on the main project building.
/// These tests verify the core workflow parsing functionality in isolation.
/// </summary>
public class WorkflowAnalyzerStandaloneTests : IDisposable
{
    private readonly string _testWorkflowDir;
    private readonly WorkflowAnalyzerService _service;

    public WorkflowAnalyzerStandaloneTests()
    {
        _service = new WorkflowAnalyzerService();
        _testWorkflowDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), ".github", "workflows");
        Directory.CreateDirectory(_testWorkflowDir);

        // Create test workflow files
        CreateTestWorkflows();
    }

    public void Dispose()
    {
        var rootDir = Path.GetDirectoryName(Path.GetDirectoryName(_testWorkflowDir));
        if (rootDir != null && Directory.Exists(rootDir))
        {
            Directory.Delete(rootDir, true);
        }
    }

    private void CreateTestWorkflows()
    {
        File.WriteAllText(Path.Combine(_testWorkflowDir, "build.yml"), @"
name: Build
on: [push, pull_request]
env:
  DOTNET_VERSION: '9.0.x'
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet build
      - run: dotnet test
");

        File.WriteAllText(Path.Combine(_testWorkflowDir, "deploy.yml"), @"
name: Deploy
on:
  push:
    tags:
      - 'v*'
jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: production
    env:
      DEPLOY_ENV: production
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - name: Push to NuGet
        run: dotnet nuget push **/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }}
");
    }

    [Fact]
    public async Task FindWorkflowsAsync_FindsYmlFiles()
    {
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(_testWorkflowDir))!;
        var workflows = await _service.FindWorkflowsAsync(projectRoot);

        Assert.Equal(2, workflows.Count);
        Assert.Contains(workflows, w => w.EndsWith("build.yml"));
        Assert.Contains(workflows, w => w.EndsWith("deploy.yml"));
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsBasicInfo()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.Equal("Build", workflow.Name);
        Assert.Equal(buildPath, workflow.FilePath);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsTriggers()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.NotNull(workflow.Triggers);
        Assert.Equal(2, workflow.Triggers.Count);
        Assert.Contains("push", workflow.Triggers);
        Assert.Contains("pull_request", workflow.Triggers);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsEnvironmentVariables()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.NotNull(workflow.Environment);
        Assert.True(workflow.Environment.ContainsKey("DOTNET_VERSION"));
        Assert.Equal("9.0.x", workflow.Environment["DOTNET_VERSION"]);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsJobs()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.NotNull(workflow.Jobs);
        Assert.Single(workflow.Jobs);

        var job = workflow.Jobs[0];
        Assert.Equal("build", job.Name);
        Assert.Equal("ubuntu-latest", job.RunsOn);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsSteps()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        var job = workflow.Jobs![0];
        Assert.NotNull(job.Steps);
        Assert.Equal(4, job.Steps.Count);

        Assert.Equal("actions/checkout@v4", job.Steps[0].Uses);
        Assert.Equal("actions/setup-dotnet@v4", job.Steps[1].Uses);
        Assert.Equal("dotnet build", job.Steps[2].Run);
        Assert.Equal("dotnet test", job.Steps[3].Run);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsStepParameters()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        var setupStep = workflow.Jobs![0].Steps![1];
        Assert.NotNull(setupStep.With);
        Assert.True(setupStep.With.ContainsKey("dotnet-version"));
        Assert.Equal("9.0.x", setupStep.With["dotnet-version"]);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsJobLevelEnvironment()
    {
        var deployPath = Path.Combine(_testWorkflowDir, "deploy.yml");
        var workflow = await _service.ParseWorkflowAsync(deployPath);

        var job = workflow.Jobs![0];
        Assert.NotNull(job.Environment);
        Assert.True(job.Environment.ContainsKey("DEPLOY_ENV"));
        Assert.Equal("production", job.Environment["DEPLOY_ENV"]);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsSecrets()
    {
        var deployPath = Path.Combine(_testWorkflowDir, "deploy.yml");
        var workflow = await _service.ParseWorkflowAsync(deployPath);

        Assert.NotNull(workflow.Secrets);
        Assert.Single(workflow.Secrets);
        Assert.Contains("NUGET_API_KEY", workflow.Secrets);
    }

    [Fact]
    public async Task GetAllWorkflowsAsync_ReturnsAllWorkflows()
    {
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(_testWorkflowDir))!;
        var workflows = await _service.GetAllWorkflowsAsync(projectRoot);

        Assert.Equal(2, workflows.Count);
        Assert.Contains(workflows, w => w.Name == "Build");
        Assert.Contains(workflows, w => w.Name == "Deploy");
    }

    [Fact]
    public void Constructor_CreatesServiceSuccessfully()
    {
        var service = new WorkflowAnalyzerService();
        Assert.NotNull(service);
    }
}
