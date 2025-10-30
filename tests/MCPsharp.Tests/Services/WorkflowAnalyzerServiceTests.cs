using MCPsharp.Models;
using MCPsharp.Services;
using Xunit;

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

    [Fact]
    public async Task FindWorkflowsAsync_WithNoWorkflowsDirectory_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = await _service.FindWorkflowsAsync(nonExistentPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
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
            Assert.Equal(2, result.Count);
            Assert.All(result, path => Assert.True(path.EndsWith(".yml") || path.EndsWith(".yaml")));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task ParseWorkflowAsync_WithValidWorkflow_ParsesCorrectly()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.Equal("Build", result.Name);
        Assert.Equal(workflowPath, result.FilePath);
        Assert.NotNull(result.Triggers);
        Assert.Contains("push", result.Triggers);
        Assert.Contains("pull_request", result.Triggers);
    }

    [Fact]
    public async Task ParseWorkflowAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDataPath, "nonexistent.yml");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _service.ParseWorkflowAsync(nonExistentPath));
    }

    [Fact]
    public async Task ParseWorkflowAsync_WithInvalidYaml_ThrowsException()
    {
        // Arrange
        var invalidPath = Path.Combine(_testDataPath, "invalid.yml");

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            async () => await _service.ParseWorkflowAsync(invalidPath));
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsTriggers_FromSimpleList()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.NotNull(result.Triggers);
        Assert.Equal(2, result.Triggers.Count);
        Assert.Contains("push", result.Triggers);
        Assert.Contains("pull_request", result.Triggers);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsTriggers_FromDictionary()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "matrix-build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.NotNull(result.Triggers);
        Assert.Contains("push", result.Triggers);
        Assert.Contains("pull_request", result.Triggers);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsEnvironmentVariables()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.NotNull(result.Environment);
        Assert.Contains("DOTNET_VERSION", result.Environment.Keys);
        Assert.Equal("9.0.x", result.Environment["DOTNET_VERSION"]);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsJobs()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.NotNull(result.Jobs);
        Assert.Single(result.Jobs);
        Assert.Equal("build", result.Jobs[0].Name);
        Assert.Equal("ubuntu-latest", result.Jobs[0].RunsOn);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.NotNull(job);
        Assert.NotNull(job.Steps);
        Assert.Equal(4, job.Steps.Count);

        var checkoutStep = job.Steps[0];
        Assert.Equal("actions/checkout@v4", checkoutStep.Uses);

        var setupDotnetStep = job.Steps[1];
        Assert.Equal("actions/setup-dotnet@v4", setupDotnetStep.Uses);
        Assert.NotNull(setupDotnetStep.With);
        Assert.Equal("9.0.x", setupDotnetStep.With["dotnet-version"]);

        var buildStep = job.Steps[2];
        Assert.Equal("dotnet build", buildStep.Run);

        var testStep = job.Steps[3];
        Assert.Equal("dotnet test", testStep.Run);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsSecrets()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "deploy.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        Assert.NotNull(result.Secrets);
        Assert.Equal(2, result.Secrets.Count);
        Assert.Contains("NUGET_API_KEY", result.Secrets);
        Assert.Contains("DEPLOY_TOKEN", result.Secrets);
    }

    [Fact]
    public async Task ParseWorkflowAsync_ExtractsJobLevelEnvironment()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "deploy.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.NotNull(job);
        Assert.NotNull(job.Environment);
        Assert.Contains("DEPLOY_ENV", job.Environment.Keys);
        Assert.Equal("production", job.Environment["DEPLOY_ENV"]);
    }

    [Fact]
    public async Task ParseWorkflowAsync_WithNamedSteps_ExtractsStepNames()
    {
        // Arrange
        var workflowPath = Path.Combine(_testDataPath, "matrix-build.yml");

        // Act
        var result = await _service.ParseWorkflowAsync(workflowPath);

        // Assert
        var job = result.Jobs?.FirstOrDefault();
        Assert.NotNull(job);
        Assert.NotNull(job.Steps);

        var checkoutStep = job.Steps.FirstOrDefault(s => s.Name == "Checkout code");
        Assert.NotNull(checkoutStep);
        Assert.Equal("actions/checkout@v4", checkoutStep.Uses);

        var setupStep = job.Steps.FirstOrDefault(s => s.Name == "Setup .NET");
        Assert.NotNull(setupStep);
    }

    [Fact]
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
            Assert.Equal(2, result.Count);
            Assert.Contains(result, w => w.Name == "Build");
            Assert.Contains(result, w => w.Name == "Deploy");
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
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
        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateWorkflowConsistencyAsync_WithMismatchedVersions_ReturnsWarning()
    {
        // Arrange
        var workflow = new WorkflowInfo
        {
            Name = "Test",
            FilePath = "/test/workflow.yml",
            Environment = new Dictionary<string, string> { { "DOTNET_VERSION", "8.0.x" } },
            Jobs = Array.Empty<WorkflowJob>()
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
        Assert.Single(result);
        Assert.Equal("VersionMismatch", result[0].Type);
        Assert.Equal("Warning", result[0].Severity);
        Assert.Contains("8.0", result[0].Message);
        Assert.Contains("net9.0", result[0].Message);
    }

    [Fact]
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
        Assert.Contains(result, issue => issue.Type == "MissingBuildStep");
        var buildStepIssue = result.First(issue => issue.Type == "MissingBuildStep");
        Assert.Equal("Warning", buildStepIssue.Severity);
        Assert.Contains("dotnet build", buildStepIssue.Message);
    }

    [Fact]
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
        Assert.DoesNotContain(result, issue => issue.Type == "MissingBuildStep");
    }
}
