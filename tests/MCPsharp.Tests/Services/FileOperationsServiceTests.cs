using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;
using FileInfo = MCPsharp.Models.FileInfo;

namespace MCPsharp.Tests.Services;

[TestFixture]
public class FileOperationsServiceTests
{
    private string _testRoot = null!;
    private FileOperationsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary test directory
        _testRoot = Path.Combine(Path.GetTempPath(), $"mcpsharp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _service = new FileOperationsService(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public void Constructor_ShouldThrowException_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => new FileOperationsService(nonExistentPath));
    }

    [Test]
    public void ListFiles_ShouldReturnAllFiles_WhenNoPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles();

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(3));
        Assert.That(result.Files, Has.Count.EqualTo(3));
        Assert.That(result.Pattern, Is.Null);
    }

    [Test]
    public void ListFiles_ShouldFilterByCsFiles_WhenPatternSpecified()
    {
        // Arrange
        CreateTestFile("file1.cs", "// test");
        CreateTestFile("file2.txt", "test");
        CreateTestFile("subdir/file3.cs", "// test");

        // Act
        var result = _service.ListFiles("**/*.cs");

        // Assert
        Assert.That(result.TotalFiles, Is.EqualTo(2));
        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files, Has.All.Matches<FileInfo>(f => f.RelativePath.EndsWith(".cs")));
        Assert.That(result.Pattern, Is.EqualTo("**/*.cs"));
    }

    [Test]
    public void ListFiles_ShouldExcludeHiddenFiles_ByDefault()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        var hiddenFile = CreateTestFile(".hidden.cs", "// test");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        // Act
        var result = _service.ListFiles();

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files, Has.All.Matches<FileInfo>(f => !f.IsHidden));
    }

    [Test]
    public void ListFiles_ShouldIncludeHiddenFiles_WhenRequested()
    {
        // Arrange
        CreateTestFile("visible.cs", "// test");
        CreateTestFile(".hidden.cs", "// test");

        // Act
        var result = _service.ListFiles(includeHidden: true);

        // Assert
        Assert.That(result.Files, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ReadFileAsync_ShouldReturnContent_WhenFileExists()
    {
        // Arrange
        var content = "Hello, World!\nLine 2\nLine 3";
        CreateTestFile("test.txt", content);

        // Act
        var result = await _service.ReadFileAsync("test.txt");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Content, Is.EqualTo(content));
        Assert.That(result.LineCount, Is.EqualTo(3));
        Assert.That(result.Encoding, Is.EqualTo("utf-8"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ReadFileAsync_ShouldFail_WhenFileDoesNotExist()
    {
        // Act
        var result = await _service.ReadFileAsync("nonexistent.txt");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public async Task ReadFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.ReadFileAsync("../outside.txt");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    [Test]
    public async Task WriteFileAsync_ShouldCreateNewFile()
    {
        // Arrange
        var content = "New file content";

        // Act
        var result = await _service.WriteFileAsync("newfile.txt", content);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);
        Assert.That(result.BytesWritten, Is.GreaterThan(0));

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "newfile.txt"));
        Assert.That(fileContent, Is.EqualTo(content));
    }

    [Test]
    public async Task WriteFileAsync_ShouldOverwriteExistingFile()
    {
        // Arrange
        CreateTestFile("existing.txt", "Old content");
        var newContent = "New content";

        // Act
        var result = await _service.WriteFileAsync("existing.txt", newContent);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.False);

        var fileContent = File.ReadAllText(Path.Combine(_testRoot, "existing.txt"));
        Assert.That(fileContent, Is.EqualTo(newContent));
    }

    [Test]
    public async Task WriteFileAsync_ShouldCreateDirectories_WhenRequested()
    {
        // Arrange
        var content = "Test content";

        // Act
        var result = await _service.WriteFileAsync("subdir1/subdir2/file.txt", content, createDirectories: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.True);

        var filePath = Path.Combine(_testRoot, "subdir1/subdir2/file.txt");
        Assert.That(File.Exists(filePath), Is.True);
    }

    [Test]
    public async Task WriteFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Act
        var result = await _service.WriteFileAsync("../outside.txt", "content");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.EditsApplied, Is.EqualTo(1));
        Assert.That(result.NewContent, Is.EqualTo("int x = 10;"));
    }

    [Test]
    public async Task EditFileAsync_ShouldApplyInsertEdit()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");

        var edits = new List<TextEdit>
        {
            new InsertEdit
            {
                StartLine = 0,
                StartColumn = 10,
                EndLine = 0,
                EndColumn = 10,
                NewText = " // comment"
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("int x = 5; // comment"));
    }

    [Test]
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
                EndColumn = 25,  // String length is 25
                NewText = ""
            }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("int x = 5; "));
    }

    [Test]
    public async Task EditFileAsync_ShouldApplyMultipleEdits_InCorrectOrder()
    {
        // Arrange
        var content = "Line 1\nLine 2\nLine 3";
        CreateTestFile("test.txt", content);

        var edits = new List<TextEdit>
        {
            new InsertEdit { StartLine = 0, StartColumn = 6, EndLine = 0, EndColumn = 6, NewText = " INSERTED" },
            new ReplaceEdit { StartLine = 1, StartColumn = 0, EndLine = 1, EndColumn = 4, NewText = "Modified" },
            new DeleteEdit { StartLine = 2, StartColumn = 5, EndLine = 2, EndColumn = 6, NewText = "" }
        };

        // Act
        var result = await _service.EditFileAsync("test.txt", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.EditsApplied, Is.EqualTo(3));
        Assert.That(result.NewContent, Is.EqualTo("Line 1 INSERTED\nModified 2\nLine "));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("Line 1\nReplaced\nMultiple\nLines\nLine 4"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenFileDoesNotExist()
    {
        // Arrange
        var edits = new List<TextEdit>
        {
            new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = "test" }
        };

        // Act
        var result = await _service.EditFileAsync("nonexistent.txt", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenPathOutsideRoot()
    {
        // Arrange
        var edits = new List<TextEdit>
        {
            new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = "test" }
        };

        // Act
        var result = await _service.EditFileAsync("../outside.txt", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside project root"));
    }

    // Column Indexing Validation Tests

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartLineIsNegative()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = -1, StartColumn = 0, EndLine = 0, EndColumn = 1, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("StartLine -1 is out of range"));
        Assert.That(result.Error, Does.Contain("Invalid edit position"));
        Assert.That(result.Error, Does.Contain("0-based indexing"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartLineExceedsFileLength()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 5, StartColumn = 0, EndLine = 5, EndColumn = 1, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("StartLine 5 is out of range"));
        Assert.That(result.Error, Does.Contain("File has 1 lines (0-0)"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenEndLineExceedsFileLength()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = 0, EndLine = 5, EndColumn = 1, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("EndLine 5 is out of range"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartColumnIsNegative()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = -1, EndLine = 0, EndColumn = 1, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("StartColumn -1 is out of range for line 0"));
        Assert.That(result.Error, Does.Contain("Line 0 has 10 characters. Valid positions: 0-9 for replacements, 0-10 for insertions."));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartColumnExceedsLineLength()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = 15, EndLine = 0, EndColumn = 16, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("StartColumn 15 is out of range for line 0"));
        Assert.That(result.Error, Does.Contain("Line 0 has 10 characters. Valid positions: 0-9 for replacements, 0-10 for insertions."));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenEndColumnExceedsLineLength()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = 5, EndLine = 0, EndColumn = 15, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("EndColumn 15 is out of range for line 0"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartColumnGreaterThanEndColumn()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = 8, EndLine = 0, EndColumn = 5, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Invalid edit range: start position (0,8) is after end position (0,5)"));
    }

    [Test]
    public async Task EditFileAsync_ShouldFail_WhenStartLineGreaterThanEndLine()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;\nint y = 10;");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 1, StartColumn = 0, EndLine = 0, EndColumn = 1, NewText = "y" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Invalid edit range: start position (1,0) is after end position (0,1)"));
    }

    [Test]
    public async Task EditFileAsync_ShouldSucceed_WhenEditAtValidBounds()
    {
        // Arrange - test editing at exact line boundaries
        CreateTestFile("test.cs", "abc");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 3, NewText = "xyz" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("xyz"));
    }

    [Test]
    public async Task EditFileAsync_ShouldSucceed_WhenEditAtStartOfLine()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = "// comment\n" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("// comment\nint x = 5;"));
    }

    [Test]
    public async Task EditFileAsync_ShouldSucceed_WhenEditAtEndOfLine()
    {
        // Arrange
        CreateTestFile("test.cs", "int x = 5;");
        var edits = new List<TextEdit>
        {
            new InsertEdit { StartLine = 0, StartColumn = 10, EndLine = 0, EndColumn = 10, NewText = " // comment" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("int x = 5; // comment"));
    }

    [Test]
    public async Task EditFileAsync_ShouldSucceed_WhenEditEmptyLine()
    {
        // Arrange
        CreateTestFile("test.cs", "\n");
        var edits = new List<TextEdit>
        {
            new ReplaceEdit { StartLine = 1, StartColumn = 0, EndLine = 1, EndColumn = 0, NewText = "content" }
        };

        // Act
        var result = await _service.EditFileAsync("test.cs", edits);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.NewContent, Is.EqualTo("\ncontent"));
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
