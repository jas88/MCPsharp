using System.Text;
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
public class McpServerIntegrationTests
{
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
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f.RelativePath.Contains("Program.cs")));
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f.RelativePath.Contains("Helper.cs")));
        Assert.That(listResult.Files, Has.Some.Matches<FileInfo>(f => f.RelativePath.Contains("README.md")));
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
        Assert.That(listResult.Files, Has.All.Matches<FileInfo>(f => f.RelativePath.EndsWith(".cs")));
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

    [Test]
    public async Task Test06_MultipleOperations_SequentialReads()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Read multiple files
        var programResult = await _fileService.ReadFileAsync("src/Program.cs");
        var helperResult = await _fileService.ReadFileAsync("src/Helper.cs");
        var readmeResult = await _fileService.ReadFileAsync("README.md");

        // Assert - All reads succeeded
        Assert.That(programResult.Success, Is.True);
        Assert.That(programResult.Content, Does.Contain("Main"));

        Assert.That(helperResult.Success, Is.True);
        Assert.That(helperResult.Content, Does.Contain("Helper"));

        Assert.That(readmeResult.Success, Is.True);
        Assert.That(readmeResult.Content, Does.Contain("Test Project"));
    }

    [Test]
    public async Task Test07_ErrorHandling_ReadNonExistentFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act
        var result = await _fileService.ReadFileAsync("src/DoesNotExist.cs");

        // Assert - Proper error response
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public async Task Test08_ErrorHandling_ReadFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to read outside project root
        var result = await _fileService.ReadFileAsync("../outside.cs");

        // Assert - Security error
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public void Test09_ErrorHandling_OpenNonExistentProject()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        var ex = Assert.Throws<DirectoryNotFoundException>(() => new FileOperationsService(nonExistentPath));
        Assert.That(ex.Message, Is.EqualTo($"Root path does not exist: {nonExistentPath}"));
    }

    [Test]
    public async Task Test10_ErrorHandling_WriteFileOutsideProject()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Try to write outside project root
        var result = await _fileService.WriteFileAsync("../outside.cs", "content");

        // Assert - Security error
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    [Test]
    public async Task Test11_CompleteWorkflow_CreateEditRead()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Workflow.cs";
        var initialContent = "namespace TestProject\n{\n    public class Workflow { }\n}";

        // Act 1 - Create file
        var createResult = await _fileService.WriteFileAsync(fileName, initialContent);
        Assert.That(createResult.Success, Is.True);
        Assert.That(createResult.Created, Is.True);

        // Act 2 - Edit file (add method before closing brace)
        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                StartLine = 2,
                StartColumn = 28, // Position right before closing brace (length of "    public class Workflow { }")
                EndLine = 2,
                EndColumn = 28,
                NewText = "\n        public void Execute() { }\n    "
            }
        };
        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Debug: print error if failed
        if (!editResult.Success)
        {
            throw new Exception($"Edit failed: {editResult.Error}");
        }
        Assert.That(editResult.Success, Is.True);

        // Act 3 - Read back final content
        var readResult = await _fileService.ReadFileAsync(fileName);

        // Assert - Verify complete workflow
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Does.Contain("public class Workflow"));
        Assert.That(readResult.Content, Does.Contain("public void Execute()"));
    }

    [Test]
    public void Test12_GlobPatterns_CsprojFiles()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Find all .csproj files
        var result = _fileService.ListFiles("**/*.csproj");

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(1));
        Assert.That(result.Files, Has.Exactly(1).Matches<FileInfo>(f => f.RelativePath.Contains("TestProject.csproj")));
    }

    [Test]
    public void Test13_GlobPatterns_TestFiles()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Find all test files
        var result = _fileService.ListFiles("tests/**/*.cs");

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(1));
        Assert.That(result.Files, Has.Exactly(1).Matches<FileInfo>(f => f.RelativePath.Contains("ProgramTests.cs")));
    }

    [Test]
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
            new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = "// Header\n" },
            // Replace Line 3
            new ReplaceEdit { StartLine = 2, StartColumn = 0, EndLine = 2, EndColumn = 6, NewText = "Modified Line 3" },
            // Delete part of Line 5
            new DeleteEdit { StartLine = 4, StartColumn = 0, EndLine = 4, EndColumn = 5, NewText = "" }
        };

        var editResult = await _fileService.EditFileAsync(fileName, edits);

        // Assert
        Assert.That(editResult.Success, Is.True);
        Assert.That(editResult.EditsApplied, Is.EqualTo(3));
        Assert.That(editResult.NewContent, Does.Contain("// Header"));
        Assert.That(editResult.NewContent, Does.Contain("Modified Line 3"));
    }

    [Test]
    public async Task Test15_DirectoryCreation_NestedDirectories()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/nested/deep/File.cs";
        var content = "namespace Deep { }";

        // Act - Write to nested path (should create directories)
        var result = await _fileService.WriteFileAsync(fileName, content, createDirectories: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);
        Assert.That(File.Exists(Path.Combine(_testProjectRoot, fileName)), Is.True);

        // Verify can read it back
        var readResult = await _fileService.ReadFileAsync(fileName);
        Assert.That(readResult.Success, Is.True);
        Assert.That(readResult.Content, Is.EqualTo(content));
    }

    [Test]
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
        Assert.That(json, Does.Contain("\"files\":"));
        Assert.That(json, Does.Contain("\"totalFiles\":"));
        Assert.That(json, Does.Contain("\"pattern\":"));

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileListResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.TotalFiles, Is.EqualTo(listResult.TotalFiles));
    }

    [Test]
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
        Assert.That(json, Does.Contain("\"success\": true"));
        Assert.That(json, Does.Contain("\"content\":"));
        Assert.That(json, Does.Contain("\"encoding\":"));
        Assert.That(json, Does.Contain("\"lineCount\":"));

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileReadResult>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Success, Is.True);
        Assert.That(deserialized.Content, Is.EqualTo(readResult.Content));
    }

    [Test]
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
        Assert.That(result.TotalFiles, Is.GreaterThan(50));
        Assert.That(duration, Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
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
        Assert.That(results, Has.All.Matches<FileReadResult>(r => r.Success == true));
        Assert.That(results, Has.Length.EqualTo(4));
    }

    [Test]
    public async Task Test20_EdgeCase_EmptyFile()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);
        var fileName = "src/Empty.cs";
        await _fileService.WriteFileAsync(fileName, string.Empty);

        // Act - Read empty file
        var result = await _fileService.ReadFileAsync(fileName);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Is.Empty);
        Assert.That(result.LineCount, Is.EqualTo(1)); // Empty file has 1 line
        // Note: Size may be > 0 due to UTF-8 BOM, so we just check it's small
        Assert.That(result.Size, Is.LessThan(10));
    }
}
