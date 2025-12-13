using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class WorkflowAnalyzerServiceTests
{
    private readonly WorkflowAnalyzerService _service;
    private readonly string _testDataPath;

    public WorkflowAnalyzerServiceTests()
    {
        _service = new WorkflowAnalyzerService();
        _testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Workflows");
    }

    [Test]
    public async Task FindWorkflowsAsync_WithNoWorkflowsDirectory_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = await _service.FindWorkflowsAsync(nonExistentPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FindWorkflowsAsync_WithWorkflowsDirectory_ReturnsWorkflowFiles()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var workflowsPath = Path.Combine(tempPath, ".github", "workflows");
        Directory.CreateDirectory(workflowsPath);

        try
        {
            File.WriteAllText(Path.Combine(workflowsPath, "test1.yml"), "name: Test1");
            File.WriteAllText(Path.Combine(workflowsPath, "test2.yaml"), "name: Test2");
            File.WriteAllText(Path.Combine(workflowsPath, "readme.md"), "Not a workflow");

            // Act
            var result = await _service.FindWorkflowsAsync(tempPath);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Has.All.Matches<string>(path => path.EndsWith(".yml") || path.EndsWith(".yaml")));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Test]
    public async Task ParseWorkflowAsync_WithValidWorkflow_ParsesCorrectly()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Name, Is.EqualTo("Build"));
        Assert.That(result.FilePath, Is.EqualTo(workflowPath));
        Assert.That(result.Triggers, Is.Not.Null);
        Assert.That(result.Triggers, Does.Contain("push"));
        Assert.That(result.Triggers, Does.Contain("pull_request"));
    }

    [Test]
    public async Task ParseWorkflowAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDataPath, "nonexistent.yml");

        // Act & Assert
        Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _service.ParseWorkflowAsync(nonExistentPath));
    }

    [Test]
    public async Task ParseWorkflowAsync_WithInvalidYaml_ThrowsException()
    {
        // Arrange
        var invalidPath = Path.Combine(_testDataPath, "invalid.yml");

        // Act & Assert
        Assert.ThrowsAsync<YamlDotNet.Core.SemanticErrorException>(
            async () => await _service.ParseWorkflowAsync(invalidPath));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsTriggers_FromSimpleList()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Triggers, Is.Not.Null);
        Assert.That(result.Triggers, Has.Count.EqualTo(2));
        Assert.That(result.Triggers, Does.Contain("push"));
        Assert.That(result.Triggers, Does.Contain("pull_request"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsTriggers_FromDictionary()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "matrix-build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Triggers, Is.Not.Null);
        Assert.That(result.Triggers, Does.Contain("push"));
        Assert.That(result.Triggers, Does.Contain("pull_request"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsEnvironmentVariables()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Environment, Is.Not.Null);
        Assert.That(result.Environment.Keys, Does.Contain("DOTNET_VERSION"));
        Assert.That(result.Environment["DOTNET_VERSION"], Is.EqualTo("9.0.x"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsJobs()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Jobs, Is.Not.Null);
        Assert.That(result.Jobs, Has.Count.EqualTo(1));
        Assert.That(result.Jobs[0].Name, Is.EqualTo("build"));
        Assert.That(result.Jobs[0].RunsOn, Is.EqualTo("ubuntu-latest"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.That(job, Is.Not.Null);
        Assert.That(job.Steps, Is.Not.Null);
        Assert.That(job.Steps, Has.Count.EqualTo(4));

        var checkoutStep = job.Steps[0];
        Assert.That(checkoutStep.Uses, Is.EqualTo("actions/checkout@v4"));

        var setupDotnetStep = job.Steps[1];
        Assert.That(setupDotnetStep.Uses, Is.EqualTo("actions/setup-dotnet@v4"));
        Assert.That(setupDotnetStep.With, Is.Not.Null);
        Assert.That(setupDotnetStep.With["dotnet-version"], Is.EqualTo("9.0.x"));

        var buildStep = job.Steps[2];
        Assert.That(buildStep.Run, Is.EqualTo("dotnet build"));

        var testStep = job.Steps[3];
        Assert.That(testStep.Run, Is.EqualTo("dotnet test"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsSecrets()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "deploy.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.That(result.Secrets, Is.Not.Null);
        Assert.That(result.Secrets, Has.Count.EqualTo(2));
        Assert.That(result.Secrets, Does.Contain("NUGET_API_KEY"));
        Assert.That(result.Secrets, Does.Contain("DEPLOY_TOKEN"));
    }

    [Test]
    public async Task ParseWorkflowAsync_ExtractsJobLevelEnvironment()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "deploy.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.That(job, Is.Not.Null);
        Assert.That(job.Environment, Is.Not.Null);
        Assert.That(job.Environment.Keys, Does.Contain("DEPLOY_ENV"));
        Assert.That(job.Environment["DEPLOY_ENV"], Is.EqualTo("production"));
    }

    [Test]
    public async Task ParseWorkflowAsync_WithNamedSteps_ExtractsStepNames()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "matrix-build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.That(job, Is.Not.Null);
        Assert.That(job.Steps, Is.Not.Null);

        var checkoutStep = job.Steps.FirstOrDefault(s => s.Name == "Checkout code");
        Assert.That(checkoutStep, Is.Not.Null);
        Assert.That(checkoutStep.Uses, Is.EqualTo("actions/checkout@v4"));

        var setupStep = job.Steps.FirstOrDefault(s => s.Name == "Setup .NET");
        Assert.That(setupStep, Is.Not.Null);
    }

    [Test]
    public async Task GetAllWorkflowsAsync_ReturnsAllValidWorkflows()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var workflowsPath = Path.Combine(tempPath, ".github", "workflows");
        Directory.CreateDirectory(workflowsPath);

        try
        {
            File.Copy(Path.Combine(_testDataPath, "build.yml"), Path.Combine(workflowsPath, "build.yml"));
            File.Copy(Path.Combine(_testDataPath, "deploy.yml"), Path.Combine(workflowsPath, "deploy.yml"));
            File.Copy(Path.Combine(_testDataPath, "invalid.yml"), Path.Combine(workflowsPath, "invalid.yml"));

            // Act
            var result = await _service.GetAllWorkflowsAsync(tempPath);

            // Assert - should skip invalid.yml
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Has.Some.Property("Name").EqualTo("Build"));
            Assert.That(result, Has.Some.Property("Name").EqualTo("Deploy"));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Test]
    public async Task ValidateWorkflowConsistencyAsync_WithMatchingVersions_ReturnsNoIssues()
    {
        // Arrange
        var workflow = new WorkflowInfo
        {
            Name = "Test",
            FilePath = "/test/workflow.yml",
            Environment = new Dictionary<string, string> { { "DOTNET_VERSION", "9.0.x" } },
            Jobs = new[]
            {
                new WorkflowJob
                {
                    Name = "build",
                    RunsOn = "ubuntu-latest",
                    Steps = new[]
                    {
                        new WorkflowStep
                        {
                            Name = "Setup .NET",
                            Uses = "actions/setup-dotnet@v4",
                            With = new Dictionary<string, string> { { "dotnet-version", "9.0.x" } }
                        },
                        new WorkflowStep
                        {
                            Name = "Build",
                            Run = "dotnet build"
                        }
                    }
                }
            }
        };

        var project = new CsprojInfo
        {
            Name = "TestProject",
            FilePath = "/test/TestProject.csproj",
            OutputType = "Exe",
            TargetFrameworks = new[] { "net9.0" }
        };

        // Act
        var result = await _service.ValidateWorkflowConsistencyAsync(workflow, project);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ValidateWorkflowConsistencyAsync_WithMismatchedVersions_ReturnsWarning()
    {
        // Arrange
        var workflow = new WorkflowInfo
        {
            Name = "Test",
            FilePath = "/test/workflow.yml",
            Environment = new Dictionary<string, string> { { "DOTNET_VERSION", "8.0.x" } },
            Jobs = new[]
            {
                new WorkflowJob
                {
                    Name = "build",
                    RunsOn = "ubuntu-latest",
                    Steps = new[]
                    {
                        new WorkflowStep
                        {
                            Name = "Setup .NET",
                            Uses = "actions/setup-dotnet@v4",
                            With = new Dictionary<string, string> { { "dotnet-version", "8.0.x" } }
                        },
                        new WorkflowStep
                        {
                            Name = "Build",
                            Run = "dotnet build"
                        }
                    }
                }
            }
        };

        var project = new CsprojInfo
        {
            Name = "TestProject",
            FilePath = "/test/TestProject.csproj",
            OutputType = "Exe",
            TargetFrameworks = new[] { "net9.0" }
        };

        // Act
        var result = await _service.ValidateWorkflowConsistencyAsync(workflow, project);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo("VersionMismatch"));
        Assert.That(result[0].Severity, Is.EqualTo("Warning"));
        Assert.That(result[0].Message, Does.Contain("8.0"));
        Assert.That(result[0].Message, Does.Contain("net9.0"));
    }

    [Test]
    public async Task ValidateWorkflowConsistencyAsync_WithMissingBuildSteps_ReturnsWarning()
    {
        // Arrange
        var workflow = new WorkflowInfo
        {
            Name = "Test",
            FilePath = "/test/workflow.yml",
            Environment = new Dictionary<string, string> { { "DOTNET_VERSION", "9.0.x" } },
            Jobs = new[]
            {
                new WorkflowJob
                {
                    Name = "build",
                    RunsOn = "ubuntu-latest",
                    Steps = new[]
                    {
                        new WorkflowStep
                        {
                            Name = "Checkout",
                            Uses = "actions/checkout@v4"
                        }
                    }
                }
            }
        };

        var project = new CsprojInfo
        {
            Name = "TestProject",
            FilePath = "/test/TestProject.csproj",
            OutputType = "Exe",
            TargetFrameworks = new[] { "net9.0" }
        };

        // Act
        var result = await _service.ValidateWorkflowConsistencyAsync(workflow, project);

        // Assert
        Assert.That(result, Has.Some.Property("Type").EqualTo("MissingBuildStep"));
        var buildStepIssue = result.First(issue => issue.Type == "MissingBuildStep");
        Assert.That(buildStepIssue.Severity, Is.EqualTo("Warning"));
        Assert.That(buildStepIssue.Message, Does.Contain("dotnet build"));
    }

    [Test]
    public async Task ValidateWorkflowConsistencyAsync_WithLibraryProject_DoesNotRequireBuildSteps()
    {
        // Arrange
        var workflow = new WorkflowInfo
        {
            Name = "Test",
            FilePath = "/test/workflow.yml",
            Environment = new Dictionary<string, string> { { "DOTNET_VERSION", "9.0.x" } },
            Jobs = new[]
            {
                new WorkflowJob
                {
                    Name = "build",
                    RunsOn = "ubuntu-latest",
                    Steps = new[]
                    {
                        new WorkflowStep
                        {
                            Name = "Checkout",
                            Uses = "actions/checkout@v4"
                        }
                    }
                }
            }
        };

        var project = new CsprojInfo
        {
            Name = "TestLibrary",
            FilePath = "/test/TestLibrary.csproj",
            OutputType = "Library",
            TargetFrameworks = new[] { "net9.0" }
        };

        // Act
        var result = await _service.ValidateWorkflowConsistencyAsync(workflow, project);

        // Assert
        Assert.That(result, Has.None.Property("Type").EqualTo("MissingBuildStep"));
    }
}
