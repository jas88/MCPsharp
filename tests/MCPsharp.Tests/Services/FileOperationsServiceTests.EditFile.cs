using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class FileOperationsServiceTests
{
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
}
