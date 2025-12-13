using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests;

/// <summary>
/// Standalone tests for WorkflowAnalyzerService that don't depend on the main project building.
/// These tests verify the core workflow parsing functionality in isolation.
/// </summary>
[TestFixture]
public class WorkflowAnalyzerStandaloneTests
{
    private string _testWorkflowDir;
    private WorkflowAnalyzerService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new WorkflowAnalyzerService();
        _testWorkflowDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), ".github", "workflows");
        Directory.CreateDirectory(_testWorkflowDir);

        // Create test workflow files
        CreateTestWorkflows();
    }

    [TearDown]
    public void TearDown()
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

    [Test]
    public async Task FindWorkflowsAsync_FindsYmlFiles()
    {
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(_testWorkflowDir))!;
        var workflows = await _service.FindWorkflowsAsync(projectRoot);

        Assert.That(workflows.Count, Is.EqualTo(2));
        Assert.That(workflows, Has.Some.Matches<string>(w => w.EndsWith("build.yml")));
        Assert.That(workflows, Has.Some.Matches<string>(w => w.EndsWith("deploy.yml")));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsBasicInfo()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.That(workflow.Name, Is.EqualTo("Build"));
        Assert.That(workflow.FilePath, Is.EqualTo(buildPath));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsTriggers()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.That(workflow.Triggers, Is.Not.Null);
        Assert.That(workflow.Triggers.Count, Is.EqualTo(2));
        Assert.That(workflow.Triggers, Does.Contain("push"));
        Assert.That(workflow.Triggers, Does.Contain("pull_request"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsEnvironmentVariables()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.That(workflow.Environment, Is.Not.Null);
        Assert.That(workflow.Environment.ContainsKey("DOTNET_VERSION"), Is.True);
        Assert.That(workflow.Environment["DOTNET_VERSION"], Is.EqualTo("9.0.x"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsJobs()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        Assert.That(workflow.Jobs, Is.Not.Null);
        Assert.That(workflow.Jobs, Has.Count.EqualTo(1));

        var job = workflow.Jobs[0];
        Assert.That(job.Name, Is.EqualTo("build"));
        Assert.That(job.RunsOn, Is.EqualTo("ubuntu-latest"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsSteps()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        var job = workflow.Jobs![0];
        Assert.That(job.Steps, Is.Not.Null);
        Assert.That(job.Steps.Count, Is.EqualTo(4));

        Assert.That(job.Steps[0].Uses, Is.EqualTo("actions/checkout@v4"));
        Assert.That(job.Steps[1].Uses, Is.EqualTo("actions/setup-dotnet@v4"));
        Assert.That(job.Steps[2].Run, Is.EqualTo("dotnet build"));
        Assert.That(job.Steps[3].Run, Is.EqualTo("dotnet test"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsStepParameters()
    {
        var buildPath = Path.Combine(_testWorkflowDir, "build.yml");
        var workflow = await _service.ParseWorkflowAsync(buildPath);

        var setupStep = workflow.Jobs![0].Steps![1];
        Assert.That(setupStep.With, Is.Not.Null);
        Assert.That(setupStep.With.ContainsKey("dotnet-version"), Is.True);
        Assert.That(setupStep.With["dotnet-version"], Is.EqualTo("9.0.x"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsJobLevelEnvironment()
    {
        var deployPath = Path.Combine(_testWorkflowDir, "deploy.yml");
        var workflow = await _service.ParseWorkflowAsync(deployPath);

        var job = workflow.Jobs![0];
        Assert.That(job.Environment, Is.Not.Null);
        Assert.That(job.Environment.ContainsKey("DEPLOY_ENV"), Is.True);
        Assert.That(job.Environment["DEPLOY_ENV"], Is.EqualTo("production"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsSecrets()
    {
        var deployPath = Path.Combine(_testWorkflowDir, "deploy.yml");
        var workflow = await _service.ParseWorkflowAsync(deployPath);

        Assert.That(workflow.Secrets, Is.Not.Null);
        Assert.That(workflow.Secrets, Has.Count.EqualTo(1));
        Assert.That(workflow.Secrets, Does.Contain("NUGET_API_KEY"));
    }

    [Test]
    public async Task GetAllWorkflowsAsync_ReturnsAllWorkflows()
    {
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(_testWorkflowDir))!;
        var workflows = await _service.GetAllWorkflowsAsync(projectRoot);

        Assert.That(workflows.Count, Is.EqualTo(2));
        Assert.That(workflows, Has.Some.Matches<dynamic>(w => w.Name == "Build"));
        Assert.That(workflows, Has.Some.Matches<dynamic>(w => w.Name == "Deploy"));
    }

    [Test]
    public void Constructor_CreatesServiceSuccessfully()
    {
        var service = new WorkflowAnalyzerService();
        Assert.That(service, Is.Not.Null);
    }
}
