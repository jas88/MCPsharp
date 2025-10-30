using FluentAssertions;
using MCPsharp.Models;
using MCPsharp.Services;

namespace MCPsharp.Tests.Services;

public class FileOperationsServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileOperationsService _service;

    public FileOperationsServiceTests()
    {
        // Create a temporary test directory
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _service = new FileOperationsService(_testRoot);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ShouldThrowException_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => new FileOperationsService(nonExistentPath);
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void ListFiles_ShouldReturnAllFiles_WhenNoPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles();

        // Assert
        result.TotalFiles.Should().Be(3);
        result.Files.Should().HaveCount(3);
        result.Pattern.Should().BeNull();
    }

    [Fact]
    public void ListFiles_ShouldFilterByCsFiles_WhenPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles("**/*.cs");

        // Assert
        result.TotalFiles.Should().Be(2);
        result.Files.Should().HaveCount(2);
        result.Files.Should().OnlyContain(f => f.RelativePath.EndsWith(".cs"));
        result.Pattern.Should().Be("**/*.cs");
    }

    [Fact]
    public void ListFiles_ShouldExcludeHiddenFiles_ByDefault()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        var hiddenFile = CreateTestFile(".hidden.cs", "// test");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        // Act
        var result = _service.ListFiles();

        // Assert
        result.Files.Should().HaveCount(1);
        result.Files.Should().OnlyContain(f => !f.IsHidden);
    }

    [Fact]
    public void ListFiles_ShouldIncludeHiddenFiles_WhenRequested()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        CreateTestFile(".hidden.cs", "// test");

        // Act
        var result = _service.ListFiles(includeHidden: true);

        // Assert
        result.Files.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadFileAsync_ShouldReturnContent_WhenFileExists()
    {
        // Arrange
        var content = "Hello, World!\nLine 2\nLine 3";
        CreateTestFile("test.txt", content);

        // Act
        var result = await _service.ReadFileAsync("test.txt");

        // Assert
        result.Success.Should().BeTrue();
        result.Content.Should().Be(content);
        result.LineCount.Should().Be(3);
        result.Encoding.Should().Be("utf-8");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ReadFileAsync_ShouldFail_WhenFileDoesNotExist()
    {
        // Act
        var result = await _service.ReadFileAsync("nonexistent.txt");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
        result.Content.Should().BeNull();
    }

    [Fact]
    public async Task ReadFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.ReadFileAsync("../outside.txt");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside project root");
    }

    [Fact]
    public async Task WriteFileAsync_ShouldCreateNewFile()
    {
        // Arrange
        var content = "New file content";

        // Act
        var result = await _service.WriteFileAsync("newfile.txt", content);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().BeTrue();
        result.BytesWritten.Should().BeGreaterThan(0);

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "newfile.txt"));
        fileContent.Should().Be(content);
    }

    [Fact]
    public async Task WriteFileAsync_ShouldOverwriteExistingFile()
    {
        // Arrange
        CreateTestFile("existing.txt", "Old content");
        var newContent = "New content";

        // Act
        var result = await _service.WriteFileAsync("existing.txt", newContent);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().BeFalse();

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "existing.txt"));
        fileContent.Should().Be(newContent);
    }

    [Fact]
    public async Task WriteFileAsync_ShouldCreateDirectories_WhenRequested()
    {
        // Arrange
        var content = "Test content";

        // Act
        var result = await _service.WriteFileAsync("subdir1/subdir2/file.txt", content, createDirectories: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().BeTrue();

        var filePath = Path.Combine(_testRoot, "subdir1/subdir2/file.txt");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.WriteFileAsync("../outside.txt", "content");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside project root");
    }

    [Fact]
    public async Task EditFileAsync_ShouldApplyReplaceEdit_SingleLine()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");

        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 0,
                StartColumn = 8,
                EndLine = 0,
                EndColumn = 9,
                NewText = "10"
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        result.Success.Should().BeTrue();
        result.EditsApplied.Should().Be(1);
        result.NewContent.Should().Be("int x = 10;");
    }

    [Fact]
    public async Task EditFileAsync_ShouldApplyInsertEdit()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");

        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                Line = 0,
                Column = 10,
                Text = " // comment"
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        result.Success.Should().BeTrue();
        result.NewContent.Should().Be("int x = 5; // comment");
    }

    [Fact]
    public async Task EditFileAsync_ShouldApplyDeleteEdit()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5; // old comment");

        var edits = new List<TextEdit>
        {
            new DeleteEdit
            {
                StartLine = 0,
                StartColumn = 11,
                EndLine = 0,
                EndColumn = 25  // String length is 25
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        result.Success.Should().BeTrue();
        result.NewContent.Should().Be("int x = 5; ");
    }

    [Fact]
    public async Task EditFileAsync_ShouldApplyMultipleEdits_InCorrectOrder()
    {
        // Arrange
        var content = "Line 1\nLine 2\nLine 3";
        CreateTestFile("test.txt", content);

        var edits = new List<TextEdit>
        {
            new InsertEdit { Line = 0, Column = 6, Text = " INSERTED" },
            new ReplaceEdit { StartLine = 1, StartColumn = 0, EndLine = 1, EndColumn = 4, NewText = "Modified" },
            new DeleteEdit { StartLine = 2, StartColumn = 5, EndLine = 2, EndColumn = 6 }
        };

        // Act
        var result = await _service.EditFileAsync("test.txt", edits);

        // Assert
        result.Success.Should().BeTrue();
        result.EditsApplied.Should().Be(3);
        result.NewContent.Should().Be("Line 1 INSERTED\nModified 2\nLine ");
    }

    [Fact]
    public async Task EditFileAsync_ShouldApplyMultiLineReplace()
    {
        // Arrange
        var content = "Line 1\nLine 2\nLine 3\nLine 4";
        CreateTestFile("test.txt", content);

        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 1,
                StartColumn = 0,
                EndLine = 2,
                EndColumn = 6,
                NewText = "Replaced\nMultiple\nLines"
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.txt", edits);

        // Assert
        result.Success.Should().BeTrue();
        result.NewContent.Should().Be("Line 1\nReplaced\nMultiple\nLines\nLine 4");
    }

    [Fact]
    public async Task EditFileAsync_ShouldFail_WhenFileDoesNotExist()
    {
        // Arrange
        var edits = new List<TextEdit>
        {
            new InsertEdit { Line = 0, Column = 0, Text = "test" }
        };

        // Act
        var result = await _service.EditFileAsync("nonexistent.txt", edits);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task EditFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Arrange
        var edits = new List<TextEdit>
        {
            new InsertEdit { Line = 0, Column = 0, Text = "test" }
        };

        // Act
        var result = await _service.EditFileAsync("../outside.txt", edits);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside project root");
    }

    private string CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
