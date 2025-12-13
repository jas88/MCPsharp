using MCPsharp.Services.Roslyn;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// Integration tests for RoslynWorkspace initialization using MSBuildWorkspace
/// </summary>
public class RoslynWorkspaceInitializationTests
{
    [Test]
    public async Task InitializeAsync_WithRealProject_LoadsWithoutErrors()
    {
        // Arrange
        var projectPath = GetProjectPath();
        if (!Directory.Exists(projectPath))
        {
            // Skip test if project directory not found
            Assert.Ignore("Project directory not found");
            return;
        }

        var workspace = new RoslynWorkspace();

        // Act
        await workspace.InitializeAsync(projectPath);
        var health = workspace.GetHealth();

        // Assert
        Assert.That(health.IsInitialized, Is.True, "Workspace should be initialized");
        Assert.That(health.LoadedProjects > 0, Is.True, "At least one project should be loaded");
        Assert.That(health.TotalFiles > 0, Is.True, "Should have source files");
        Assert.That(health.ParseableFiles > 0, Is.True, "Should have parseable files");

        // The key assertion: error count should match actual build (0 errors)
        // Previously this was 179 errors due to missing NuGet references
        Assert.That(health.ErrorCount < 50, Is.True,
            $"Error count should be low (< 50), but got {health.ErrorCount}. " +
            $"This indicates MSBuildWorkspace is loading references correctly.");

        // Verify semantic operations are available or close to being available
        Assert.That(health.CanDoSyntaxOperations, Is.True, "Should be able to do syntax operations");

        workspace.Dispose();
    }

    [Test]
    public async Task InitializeAsync_WithRealProject_CanFindSymbols()
    {
        // Arrange
        var projectPath = GetProjectPath();
        if (!Directory.Exists(projectPath))
        {
            Assert.Ignore("Project directory not found");
            return;
        }

        var workspace = new RoslynWorkspace();
        await workspace.InitializeAsync(projectPath);

        // Act
        var compilation = workspace.GetCompilation();

        // Assert
        Assert.That(compilation, Is.Not.Null);
        Assert.That(compilation.References.Count() > 5, Is.True,
            $"Should have many references from NuGet packages, got {compilation.References.Count()}");

        // Verify we can find a known type (proves references are loaded)
        var symbolsWithName = compilation.GetSymbolsWithName(
            n => n == "RoslynWorkspace",
            Microsoft.CodeAnalysis.SymbolFilter.Type);

        Assert.That(symbolsWithName, Is.Not.Empty);

        workspace.Dispose();
    }

    [Test]
    public async Task InitializeAsync_WithSolutionFile_LoadsSuccessfully()
    {
        // Arrange
        var slnPath = GetSolutionPath();
        if (!File.Exists(slnPath))
        {
            // Skip test if solution not found
            Assert.Ignore("Solution file not found");
            return;
        }

        var workspace = new RoslynWorkspace();

        // Act - pass .sln file directly (this was the regression)
        await workspace.InitializeAsync(slnPath);
        var health = workspace.GetHealth();

        // Assert
        Assert.That(health.IsInitialized, Is.True, "Workspace should be initialized from .sln file");
        Assert.That(health.LoadedProjects > 0, Is.True, "At least one project should be loaded from solution");
        Assert.That(health.TotalFiles > 0, Is.True, "Should have source files");

        // The key fix: error count should be low, not 15k
        Assert.That(health.ErrorCount < 50, Is.True,
            $"Error count should be low (< 50), but got {health.ErrorCount}. " +
            $"Regression test: Previously passing .sln file caused fallback to AdhocWorkspace with 15k errors.");

        workspace.Dispose();
    }

    [Test]
    public async Task InitializeAsync_WithCsprojFile_LoadsSuccessfully()
    {
        // Arrange
        var csprojPath = GetCsprojPath();
        if (!File.Exists(csprojPath))
        {
            // Skip test if project file not found
            Assert.Ignore("Project file not found");
            return;
        }

        var workspace = new RoslynWorkspace();

        // Act - pass .csproj file directly
        await workspace.InitializeAsync(csprojPath);
        var health = workspace.GetHealth();

        // Assert
        Assert.That(health.IsInitialized, Is.True, "Workspace should be initialized from .csproj file");
        Assert.That(health.LoadedProjects > 0, Is.True, "At least one project should be loaded");
        Assert.That(health.TotalFiles > 0, Is.True, "Should have source files");
        Assert.That(health.ErrorCount < 50, Is.True,
            $"Error count should be low (< 50), but got {health.ErrorCount}");

        workspace.Dispose();
    }

    private static string GetProjectPath()
    {
        // Navigate up from test directory to find src/MCPsharp
        var currentDir = Directory.GetCurrentDirectory();
        var testDir = Path.GetFullPath(currentDir);

        // Try to find the src/MCPsharp directory
        var repoRoot = testDir;
        while (!string.IsNullOrEmpty(repoRoot))
        {
            var srcPath = Path.Combine(repoRoot, "src", "MCPsharp");
            if (Directory.Exists(srcPath))
            {
                return srcPath;
            }
            repoRoot = Path.GetDirectoryName(repoRoot);
        }

        // Fallback: use absolute path
        return "/Users/jas88/Developer/Github/MCPsharp/src/MCPsharp";
    }

    private static string GetSolutionPath()
    {
        var repoRoot = GetRepoRoot();
        return Path.Combine(repoRoot, "MCPsharp.sln");
    }

    private static string GetCsprojPath()
    {
        var repoRoot = GetRepoRoot();
        return Path.Combine(repoRoot, "src", "MCPsharp", "MCPsharp.csproj");
    }

    private static string GetRepoRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var testDir = Path.GetFullPath(currentDir);

        // Try to find the repo root (contains MCPsharp.sln)
        var repoRoot = testDir;
        while (!string.IsNullOrEmpty(repoRoot))
        {
            if (File.Exists(Path.Combine(repoRoot, "MCPsharp.sln")))
            {
                return repoRoot;
            }
            repoRoot = Path.GetDirectoryName(repoRoot);
        }

        // Fallback: use absolute path
        return "/Users/jas88/Developer/Github/MCPsharp";
    }
}
