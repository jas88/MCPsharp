using System.Text.Json;
using MCPsharp.Services;
using MCPsharp.Models;
using NUnit.Framework;
using FileInfo = MCPsharp.Models.FileInfo;

namespace MCPsharp.Tests.Integration;

public partial class McpServerIntegrationTests
{
    [Test]
    public void Test12_GlobPatterns_CsprojFiles()
    {
        // Arrange
        _fileService = new FileOperationsService(_testProjectRoot);

        // Act - Find all .csproj files
        var result = _fileService.ListFiles("**/*.csproj");

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(1));
        Assert.That(result.Files, Has.Exactly(1).Matches<FileInfo>(f => f is not null && f.RelativePath.Contains("TestProject.csproj")));
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
        Assert.That(result.Files, Has.Exactly(1).Matches<FileInfo>(f => f is not null && f.RelativePath.Contains("ProgramTests.cs")));
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
        var json = JsonSerializer.Serialize(listResult, CamelCaseIndentedOptions);

        // Assert - Valid JSON structure
        Assert.That(json, Does.Contain("\"files\":"));
        Assert.That(json, Does.Contain("\"totalFiles\":"));
        Assert.That(json, Does.Contain("\"pattern\":"));

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileListResult>(json, CamelCaseOptions);

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
        var json = JsonSerializer.Serialize(readResult, CamelCaseIndentedOptions);

        // Assert
        Assert.That(json, Does.Contain("\"success\": true"));
        Assert.That(json, Does.Contain("\"content\":"));
        Assert.That(json, Does.Contain("\"encoding\":"));
        Assert.That(json, Does.Contain("\"lineCount\":"));

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<FileReadResult>(json, CamelCaseOptions);

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
        Assert.That(results, Has.All.Matches<FileReadResult>(r => r is not null && r.Success == true));
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
