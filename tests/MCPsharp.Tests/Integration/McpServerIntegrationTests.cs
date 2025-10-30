using System.Text;
using System.Text.Json;
using FluentAssertions;
using MCPsharp.Services;
using MCPsharp.Models;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests that test the full MCP server workflow including JSON-RPC protocol compliance
/// </summary>
public class McpServerIntegrationTests : IDisposable
{
    private readonly string _testProjectRoot;
    private FileOperationsService _fileService = null!;

    public McpServerIntegrationTests()
    {
        // Create a temporary test project with sample C# files
        _testProjectRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-integration-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectRoot);
        SetupTestProject();
    }

    public void Dispose()
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

    [Fact]
    public void Test01_BasicProjectWorkflow_OpenAndListFiles()
    {
        // Arrange - Open project (simulated via direct service instantiation)
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - List all files
        var listResult = _fileService.ListFiles();

        // Assert
        listResult.Should().NotBeNull();
        listResult.TotalFiles.Should().BeGreaterThan(0);
        listResult.Files.Should().Contain(f => f.RelativePath.Contains("Program.cs"));
        listResult.Files.Should().Contain(f => f.RelativePath.Contains("Helper.cs"));
        listResult.Files.Should().Contain(f => f.RelativePath.Contains("README.md"));
    }

    [Fact]
    public void Test02_BasicProjectWorkflow_ListCSharpFilesWithGlob()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - List only C# files
        var listResult = _fileService.ListFiles("**/*.cs");

        // Assert
        listResult.Should().NotBeNull();
        listResult.TotalFiles.Should().Be(3); // Program.cs, Helper.cs, ProgramTests.cs
        listResult.Files.Should().OnlyContain(f => f.RelativePath.EndsWith(".cs"));
        listResult.Pattern.Should().Be("**/*.cs");
    }

    [Fact]
    public async Task Test03_BasicProjectWorkflow_ReadFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Read a specific file
        var readResult = await _fileService.ReadFileAsync("src/Program.cs");

        // Assert
        readResult.Should().NotBeNull();
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().Contain("Hello, World!");
        readResult.Content.Should().Contain("namespace TestProject");
        readResult.LineCount.Should().BeGreaterThan(5);
        readResult.Encoding.Should().Be("utf-8");
        readResult.Error.Should().BeNull();
    }

    [Fact]
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
        writeResult.Should().NotBeNull();
        writeResult.Success.Should().BeTrue();
        writeResult.Created.Should().BeTrue();
        writeResult.BytesWritten.Should().BeGreaterThan(0);

        // Act - Read it back
        var readResult = await _fileService.ReadFileAsync(newFileName);

        // Assert - Content matches
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().Be(newContent);
    }

    [Fact]
    public async Task Test05_FileEditingWorkflow_EditFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Helper.cs";

        // Read original content
        var originalContent = await _fileService.ReadFileAsync(fileName);
        originalContent.Success.Should().BeTrue();

        // Act - Edit the file (add a comment)
        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                Line = 2,
                Column = 4,
                Text = "    // This is a helper class\n"
            }
        };

        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Assert - Edit succeeded
        editResult.Success.Should().BeTrue();
        editResult.EditsApplied.Should().Be(1);
        editResult.NewContent.Should().Contain("// This is a helper class");

        // Act - Read back to verify
        var readResult = await _fileService.ReadFileAsync(fileName);

        // Assert - Content was modified
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().Contain("// This is a helper class");
    }

    [Fact]
    public async Task Test06_MultipleOperations_SequentialReads()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Read multiple files
        var programResult = await _fileService.ReadFileAsync("src/Program.cs");
        var helperResult = await _fileService.ReadFileAsync("src/Helper.cs");
        var readmeResult = await _fileService.ReadFileAsync("README.md");

        // Assert - All reads succeeded
        programResult.Success.Should().BeTrue();
        programResult.Content.Should().Contain("Main");

        helperResult.Success.Should().BeTrue();
        helperResult.Content.Should().Contain("Helper");

        readmeResult.Success.Should().BeTrue();
        readmeResult.Content.Should().Contain("Test Project");
    }

    [Fact]
    public async Task Test07_ErrorHandling_ReadNonExistentFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act
        var result = await _fileService.ReadFileAsync("src/DoesNotExist.cs");

        // Assert - Proper error response
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("not found");
        result.Content.Should().BeNull();
    }

    [Fact]
    public async Task Test08_ErrorHandling_ReadFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to read outside project root
        var result = await _fileService.ReadFileAsync("../outside.cs");

        // Assert - Security error
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside project root");
        result.Content.Should().BeNull();
    }

    [Fact]
    public void Test09_ErrorHandling_OpenNonExistentProject()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => new FileOperationsService(nonExistentPath);
        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage($"Root path does not exist: {nonExistentPath}");
    }

    [Fact]
    public async Task Test10_ErrorHandling_WriteFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to write outside project root
        var result = await _fileService.WriteFileAsync("../outside.cs", "content");

        // Assert - Security error
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside project root");
    }

    [Fact]
    public async Task Test11_CompleteWorkflow_CreateEditRead()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Workflow.cs";
        var initialContent = "namespace TestProject\n{\n    public class Workflow { }\n}";

        // Act 1 - Create file
        var createResult = await _fileService.WriteFileAsync(fileName, initialContent);
        createResult.Success.Should().BeTrue();
        createResult.Created.Should().BeTrue();

        // Act 2 - Edit file (add method before closing brace)
        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                Line = 2,
                Column = 28, // Position right before closing brace (length of "    public class Workflow { }")
                Text = "\n        public void Execute() { }\n    "
            }
        };
        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Debug: print error if failed
        if (!editResult.Success)
        {
            throw new Exception($"Edit failed: {editResult.Error}");
        }
        editResult.Success.Should().BeTrue();

        // Act 3 - Read back final content
        var readResult = await _fileService.ReadFileAsync(fileName);

        // Assert - Verify complete workflow
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().Contain("public class Workflow");
        readResult.Content.Should().Contain("public void Execute()");
    }

    [Fact]
    public async Task Test12_GlobPatterns_CsprojFiles()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Find all .csproj files
        var result = _fileService.ListFiles("**/*.csproj");

        // Assert
        result.TotalFiles.Should().Be(1);
        result.Files.Should().ContainSingle(f => f.RelativePath.Contains("TestProject.csproj"));
    }

    [Fact]
    public async Task Test13_GlobPatterns_TestFiles()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Find all test files
        var result = _fileService.ListFiles("tests/**/*.cs");

        // Assert
        result.TotalFiles.Should().Be(1);
        result.Files.Should().ContainSingle(f => f.RelativePath.Contains("ProgramTests.cs"));
    }

    [Fact]
    public async Task Test14_ComplexEdits_MultipleEditsInOneFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Complex.cs";
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        await _fileService.WriteFileAsync(fileName, content);

        // Act - Apply multiple edits
        var edits = new List<TextEdit>
        {
            // Insert at beginning of Line 1
            new InsertEdit { Line = 0, Column = 0, Text = "// Header\n" },
            // Replace Line 3
            new ReplaceEdit { StartLine = 2, StartColumn = 0, EndLine = 2, EndColumn = 6, NewText = "Modified Line 3" },
            // Delete part of Line 5
            new DeleteEdit { StartLine = 4, StartColumn = 0, EndLine = 4, EndColumn = 5 }
        };

        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Assert
        editResult.Success.Should().BeTrue();
        editResult.EditsApplied.Should().Be(3);
        editResult.NewContent.Should().Contain("// Header");
        editResult.NewContent.Should().Contain("Modified Line 3");
    }

    [Fact]
    public async Task Test15_DirectoryCreation_NestedDirectories()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/nested/deep/File.cs";
        var content = "namespace Deep { }";

        // Act - Write to nested path (should create directories)
        var result = await _fileService.WriteFileAsync(fileName, content, createDirectories: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().BeTrue();
        File.Exists(Path.Combine(_testProjectRoot, fileName)).Should().BeTrue();

        // Verify can read it back
        var readResult = await _fileService.ReadFileAsync(fileName);
        readResult.Success.Should().BeTrue();
        readResult.Content.Should().Be(content);
    }

    [Fact]
    public void Test16_JsonSerialization_FileListResult()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var listResult = _fileService.ListFiles("**/*.cs");

        // Act - Serialize to JSON (simulating JSON-RPC response)
        var json = JsonSerializer.Serialize(listResult, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Assert - Valid JSON structure
        json.Should().Contain("\"files\":");
        json.Should().Contain("\"totalFiles\":");
        json.Should().Contain("\"pattern\":");

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileListResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        deserialized.Should().NotBeNull();
        deserialized!.TotalFiles.Should().Be(listResult.TotalFiles);
    }

    [Fact]
    public async Task Test17_JsonSerialization_FileReadResult()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var readResult = await _fileService.ReadFileAsync("src/Program.cs");

        // Act - Serialize to JSON
        var json = JsonSerializer.Serialize(readResult, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Assert
        json.Should().Contain("\"success\": true");
        json.Should().Contain("\"content\":");
        json.Should().Contain("\"encoding\":");
        json.Should().Contain("\"lineCount\":");

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileReadResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.Content.Should().Be(readResult.Content);
    }

    [Fact]
    public async Task Test18_Performance_ListManyFiles()
    {
        // Arrange - Create many test files
        _fileService = new FileOperationsService(_testProjectRoot);
        for (int i = 0; i < 50; i++)
        {
            await _fileService.WriteFileAsync($"src/Generated{i}.cs", $"// File {i}", createDirectories: true);
        }

        // Act - List all C# files
        var startTime = DateTime.UtcNow;
        var result = _fileService.ListFiles("**/*.cs");
        var duration = DateTime.UtcNow - startTime;

        // Assert - Should be fast
        result.TotalFiles.Should().BeGreaterThan(50);
        duration.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Test19_ConcurrentOperations_MultipleReads()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Perform multiple reads concurrently
        var tasks = new[]
        {
            _fileService.ReadFileAsync("src/Program.cs"),
            _fileService.ReadFileAsync("src/Helper.cs"),
            _fileService.ReadFileAsync("tests/ProgramTests.cs"),
            _fileService.ReadFileAsync("README.md")
        };

        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        results.Should().HaveCount(4);
    }

    [Fact]
    public async Task Test20_EdgeCase_EmptyFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Empty.cs";
        await _fileService.WriteFileAsync(fileName, string.Empty);

        // Act - Read empty file
        var result = await _fileService.ReadFileAsync(fileName);

        // Assert
        result.Success.Should().BeTrue();
        result.Content.Should().BeEmpty();
        result.LineCount.Should().Be(1); // Empty file has 1 line
        // Note: Size may be > 0 due to UTF-8 BOM, so we just check it's small
        result.Size.Should().BeLessThan(10);
    }
}
