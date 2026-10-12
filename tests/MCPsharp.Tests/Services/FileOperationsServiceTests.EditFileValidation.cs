using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class FileOperationsServiceTests
{
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
}
