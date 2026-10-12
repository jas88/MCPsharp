using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Services;
using MCPsharp.Services.Phase2;
using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Phase 2 Integration Tests - Tests complete workflows across multiple Phase 2 features
/// These tests create realistic project structures and verify end-to-end functionality
/// </summary>
[TestFixture]
[Category("Phase2Integration")]
public partial class Phase2IntegrationTests
{
    private string _testRoot;
    private RoslynWorkspace _workspace;
    private ReferenceFinderService _referenceFinder;
    private MCPsharp.Services.Phase2.WorkflowAnalyzerService _workflowAnalyzer;
    private ConfigAnalyzerService _configAnalyzer;
    private ImpactAnalyzerService _impactAnalyzer;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp_phase2_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        // Initialize Roslyn workspace and services
        _workspace = new RoslynWorkspace();
        _referenceFinder = new ReferenceFinderService(_workspace);
        _workflowAnalyzer = new MCPsharp.Services.Phase2.WorkflowAnalyzerService();
        _configAnalyzer = new ConfigAnalyzerService(NullLogger<ConfigAnalyzerService>.Instance);
        _impactAnalyzer = new ImpactAnalyzerService(_workspace, _referenceFinder, _configAnalyzer, _workflowAnalyzer);
    }

    private async Task InitializeWorkspaceForProject(string projectDir)
    {
        await _workspace.InitializeAsync(projectDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }

        _workspace?.Dispose();
    }

    #region Workflow Analysis Integration Tests

    [Test]
    public async Task WorkflowAnalysis_GetAllWorkflows_ReturnsProjectWorkflows()
    {
        // Arrange: Create project with multiple workflows
        var projectDir = CreateTestProject("WorkflowProject");
        var workflowDir = Path.Combine(projectDir, ".github", "workflows");
        Directory.CreateDirectory(workflowDir);

        await File.WriteAllTextAsync(Path.Combine(workflowDir, "ci.yml"), @"
name: CI
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: dotnet build
");

        await File.WriteAllTextAsync(Path.Combine(workflowDir, "deploy.yml"), @"
name: Deploy
on:
  push:
    branches: [main]
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - run: echo 'Deploying...'
");

        // Act: Get all workflows
        var workflows = await _workflowAnalyzer.GetAllWorkflowsAsync(projectDir);

        // Assert: Verify workflows are detected
        Assert.That(workflows, Has.Count.EqualTo(2));
        Assert.That(workflows.Any(w => w.Name == "CI"), Is.True);
        Assert.That(workflows.Any(w => w.Name == "Deploy"), Is.True);
    }

    [Test]
    public async Task WorkflowAnalysis_ParseWorkflow_ExtractsCompleteStructure()
    {
        // Arrange: Create complex workflow
        var projectDir = CreateTestProject("ParseWorkflowProject");
        var workflowPath = CreateWorkflowFile(projectDir, "complex.yml", @"
name: Complex Build
on:
  push:
    branches: [main, develop]
  pull_request:
    types: [opened, synchronize]
jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        dotnet: ['6.0.x', '8.0.x']
    steps:
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ matrix.dotnet }}
      - run: dotnet build
      - run: dotnet test
  deploy:
    needs: build
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - run: echo 'Deploying...'
");

        // Act: Parse the workflow
        var details = await _workflowAnalyzer.ParseWorkflowAsync(workflowPath);

        // Assert: Verify workflow structure is extracted
        Assert.That(details, Is.Not.Null);
        Assert.That(details.Name, Is.EqualTo("Complex Build"));
        Assert.That(details.Triggers, Is.Not.Null);
        Assert.That(details.Triggers, Does.Contain("push"));
        Assert.That(details.Triggers, Does.Contain("pull_request"));
        Assert.That(details.Jobs, Is.Not.Null);
        Assert.That(details.Jobs, Has.Some.Matches<dynamic>(j => j is not null && j.Name == "build"));
        Assert.That(details.Jobs, Has.Some.Matches<dynamic>(j => j is not null && j.Name == "deploy"));
    }

    [Test]
    public async Task WorkflowAnalysis_ValidateConsistency_DetectsVersionMismatch()
    {
        // Arrange: Create project with mismatched .NET versions
        var projectDir = CreateTestProject("MismatchProject");

        var csprojPath = Path.Combine(projectDir, "MismatchProject.csproj");
        await File.WriteAllTextAsync(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

        var workflowPath = CreateWorkflowFile(projectDir, "build.yml", @"
name: Build
on: [push]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '6.0.x'
      - run: dotnet build
");

        // Act: Validate workflow consistency
        var issues = await _workflowAnalyzer.ValidateWorkflowConsistencyAsync(workflowPath, projectDir);

        // Assert: Verify version mismatch is detected
        Assert.That(issues, Is.Not.Empty);
        Assert.That(issues, Has.Some.Matches<dynamic>(i => i is not null && (i.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) && i.Message.Contains("version mismatch"))));
    }

    #endregion

    #region Config Analysis Integration Tests

    [Test]
    public async Task ConfigAnalysis_MergeConfigs_ProducesCorrectHierarchy()
    {
        // Arrange: Create config hierarchy
        var projectDir = CreateTestProject("ConfigProject");

        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.json"), @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft"": ""Warning""
    }
  },
  ""ConnectionStrings"": {
    ""Database"": ""Server=prod;""
  }
}");

        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.Development.json"), @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Debug""
    }
  },
  ""ConnectionStrings"": {
    ""Database"": ""Server=localhost;Database=dev;""
  }
}");

        await File.WriteAllTextAsync(Path.Combine(projectDir, "appsettings.Staging.json"), @"{
  ""ConnectionStrings"": {
    ""Database"": ""Server=staging;""
  }
}");

        var configPaths = new[]
        {
            Path.Combine(projectDir, "appsettings.json"),
            Path.Combine(projectDir, "appsettings.Development.json"),
            Path.Combine(projectDir, "appsettings.Staging.json")
        };

        // Act: Merge configurations
        var mergedConfig = await _configAnalyzer.MergeConfigsAsync(configPaths);

        // Assert: Verify merged config hierarchy
        Assert.That(mergedConfig, Is.Not.Null);
        Assert.That(mergedConfig.SourceFiles, Has.Count.EqualTo(3));

        // Check that config merging worked (keys may be in different format)
        Assert.That(mergedConfig.MergedSettings, Is.Not.Empty);

        // Look for keys that should exist after merging
        var logLevelDefault = mergedConfig.MergedSettings.Keys.FirstOrDefault(k => k.Contains("Default") && k.Contains("Logging"));
        var logLevelMicrosoft = mergedConfig.MergedSettings.Keys.FirstOrDefault(k => k.Contains("Microsoft") && k.Contains("Logging"));
        var databaseConnection = mergedConfig.MergedSettings.Keys.FirstOrDefault(k => k.Contains("Database") && k.Contains("Connection"));

        Assert.That(logLevelDefault, Is.Not.Null);
        Assert.That(logLevelMicrosoft, Is.Not.Null);
        Assert.That(databaseConnection, Is.Not.Null);

        // Development config should override default log level to Debug
        Assert.That(mergedConfig.MergedSettings[logLevelDefault], Is.EqualTo("Debug"));

        // Staging config should override connection string
        Assert.That(mergedConfig.MergedSettings[databaseConnection], Is.EqualTo("Server=staging;"));
    }

    [Test]
    public async Task ConfigAnalysis_GetSchema_ExtractsStructure()
    {
        // Arrange: Create nested config
        var projectDir = CreateTestProject("SchemaProject");
        var configPath = Path.Combine(projectDir, "appsettings.json");

        await File.WriteAllTextAsync(configPath, @"{
  ""Database"": {
    ""ConnectionString"": ""Server=localhost"",
    ""MaxConnections"": 100,
    ""Timeout"": 30,
    ""EnableRetry"": true
  },
  ""Features"": {
    ""EnableCache"": true,
    ""CacheDuration"": 3600,
    ""MaxCacheSize"": 1000
  },
  ""Api"": {
    ""BaseUrl"": ""https://api.example.com"",
    ""Endpoints"": {
      ""Users"": ""/api/users"",
      ""Products"": ""/api/products""
    }
  }
}");

        // Act: Extract configuration schema
        var schema = await _configAnalyzer.GetConfigSchemaAsync(configPath);

        // Assert: Verify schema structure is extracted with types
        Assert.That(schema, Is.Not.Null);
        Assert.That(schema.FilePath, Is.EqualTo(configPath));
        Assert.That(schema.Properties, Is.Not.Empty);

        // Verify nested structure extraction (handle potential double prefixing)
        var dbConnectionString = schema.Properties.Keys.FirstOrDefault(k => k.Contains("ConnectionString"));
        var dbMaxConnections = schema.Properties.Keys.FirstOrDefault(k => k.Contains("MaxConnections"));
        var dbTimeout = schema.Properties.Keys.FirstOrDefault(k => k.Contains("Timeout"));
        var dbEnableRetry = schema.Properties.Keys.FirstOrDefault(k => k.Contains("EnableRetry"));

        Assert.That(dbConnectionString, Is.Not.Null);
        Assert.That(dbMaxConnections, Is.Not.Null);
        Assert.That(dbTimeout, Is.Not.Null);
        Assert.That(dbEnableRetry, Is.Not.Null);

        // Verify type detection
        Assert.That(schema.Properties[dbConnectionString].Type, Is.EqualTo("string"));
        Assert.That(schema.Properties[dbMaxConnections].Type, Is.EqualTo("integer"));
        Assert.That(schema.Properties[dbTimeout].Type, Is.EqualTo("integer"));
        Assert.That(schema.Properties[dbEnableRetry].Type, Is.EqualTo("boolean"));
    }

    #endregion

    #region Helper Methods

    private string CreateTestProject(string name)
    {
        var projectDir = Path.Combine(_testRoot, name);
        Directory.CreateDirectory(projectDir);
        return projectDir;
    }

    private static string CreateWorkflowFile(string projectDir, string fileName, string content)
    {
        var workflowDir = Path.Combine(projectDir, ".github", "workflows");
        Directory.CreateDirectory(workflowDir);

        var filePath = Path.Combine(workflowDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    #endregion
}
