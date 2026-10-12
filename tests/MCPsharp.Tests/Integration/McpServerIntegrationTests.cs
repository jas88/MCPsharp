using System.Text.Json;
using MCPsharp.Services;
using MCPsharp.Models;
using NUnit.Framework;
using FileInfo = MCPsharp.Models.FileInfo;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests that test the full MCP server workflow including JSON-RPC protocol compliance
/// </summary>
[TestFixture]
public partial class McpServerIntegrationTests
{
    private static readonly JsonSerializerOptions CamelCaseIndentedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string _testProjectRoot = null!;
    private FileOperationsService _fileService = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary test project with sample C# files
        _testProjectRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-integration-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectRoot);
        SetupTestProject();
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test project
        if (Directory.Exists(_testProjectRoot))
        {
            Directory.Delete(_testProjectRoot, recursive: true);
        }
    }

    private void SetupTestProject()
    {
        // Create directory structure
        Directory.CreateDirectory(Path.Combine(_testProjectRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_testProjectRoot, "tests"));
        Directory.CreateDirectory(Path.Combine(_testProjectRoot, "docs"));

        // Create sample C# files
        File.WriteAllText(
            Path.Combine(_testProjectRoot, "src", "Program.cs"),
            """
            using System;

            namespace TestProject
            {
                class Program
                {
                    static void Main(string[] args)
                    {
                        Console.WriteLine("Hello, World!");
                    }
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(_testProjectRoot, "src", "Helper.cs"),
            """
            namespace TestProject
            {
                public class Helper
                {
                    public static string GetMessage() => "Helper message";
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(_testProjectRoot, "tests", "ProgramTests.cs"),
            """
            using Xunit;

            namespace TestProject.Tests
            {
                public class ProgramTests
                {
                    [Fact]
                    public void Test_Helper() { }
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(_testProjectRoot, "README.md"),
            "# Test Project\n\nThis is a test project.");

        File.WriteAllText(
            Path.Combine(_testProjectRoot, "TestProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }

    [Test]
    public void Test01_BasicProjectWorkflow_OpenAndListFiles()
    {
        // Arrange - Open project (simulated via direct service instantiation)
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - List all files
        var listResult = _fileService.ListFiles();

        // Assert
        Assert.That(listResult, Is.Not.Null);
        Assert.That(listResult.TotalFiles, Is.GreaterThan(0));
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f is not null && f.RelativePath.Contains("Program.cs")));
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f is not null && f.RelativePath.Contains("Helper.cs")));
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f is not null && f.RelativePath.Contains("README.md")));
    }

    [Test]
    public void Test02_BasicProjectWorkflow_ListCSharpFilesWithGlob()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - List only C# files
        var listResult = _fileService.ListFiles("**/*.cs");

        // Assert
        Assert.That(listResult, Is.Not.Null);
        Assert.That(listResult.TotalFiles, Is.EqualTo(3)); // Program.cs, Helper.cs, ProgramTests.cs
        Assert.That(listResult.Files, Has.All.Matches<FileInfo>(f => f is not null && f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)));
        Assert.That(listResult.Pattern, Is.EqualTo("**/*.cs"));
    }

    [Test]
    public async Task Test03_BasicProjectWorkflow_ReadFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Read a specific file
        var readResult = await _fileService.ReadFileAsync("src/Program.cs");

        // Assert
        Assert.That(readResult, Is.Not.Null);
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Does.Contain("Hello, World!"));
        Assert.That(readResult.Content, Does.Contain("namespace TestProject"));
        Assert.That(readResult.LineCount, Is.GreaterThan(5));
        Assert.That(readResult.Encoding, Is.EqualTo("utf-8"));
        Assert.That(readResult.Error, Is.Null);
    }

    [Test]
    public async Task Test04_FileEditingWorkflow_CreateAndReadFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var newFileName = "src/NewClass.cs";
        var newContent = """
            namespace TestProject
            {
                public class NewClass
                {
                    public int GetNumber() => 42;
                }
            }
            """;

        // Act - Create new file
        var writeResult = await _fileService.WriteFileAsync(newFileName, newContent);

        // Assert - Write succeeded
        Assert.That(writeResult, Is.Not.Null);
        Assert.That(writeResult.Success, Is.True);
        Assert.That(writeResult.Created, Is.True);
        Assert.That(writeResult.BytesWritten, Is.GreaterThan(0));

        // Act - Read it back
        var readResult = await _fileService.ReadFileAsync(newFileName);

        // Assert - Content matches
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Is.EqualTo(newContent));
    }

    [Test]
    public async Task Test05_FileEditingWorkflow_EditFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Helper.cs";

        // Read original content
        var originalContent = await _fileService.ReadFileAsync(fileName);
        Assert.That(originalContent.Success, Is.True);

        // Act - Edit the file (add a comment)
        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                StartLine = 2,
                StartColumn = 4,
                EndLine = 2,
                EndColumn = 4,
                NewText = "    // This is a helper class\n"
            }
        };

        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Assert - Edit succeeded
        Assert.That(editResult.Success, Is.True);
        Assert.That(editResult.EditsApplied, Is.EqualTo(1));
        Assert.That(editResult.NewContent, Does.Contain("// This is a helper class"));

        // Act - Read back to verify
        var readResult = await _fileService.ReadFileAsync(fileName);

        // Assert - Content was modified
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Does.Contain("// This is a helper class"));
    }
}
