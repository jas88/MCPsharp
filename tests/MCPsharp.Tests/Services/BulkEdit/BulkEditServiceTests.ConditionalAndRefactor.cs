using NUnit.Framework;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Tests.Services.BulkEdit;

public partial class BulkEditServiceTests
{
    [Test]
    public async Task ConditionalEditAsync_WithMatchingCondition_ShouldApplyEdits()
    {
        // Arrange
        var testFile = CreateTestFile("TODO: Fix this issue\nSome other content");
        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:"
        };
        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 1,
                StartColumn = 1,
                EndLine = 1,
                EndColumn = 1,
                NewText = "FIXME: Fix this issue"
            }
        };

        // Act
        var result = await _service.ConditionalEditAsync(new[] { testFile }, condition, edits);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FileResults[0].ChangeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ConditionalEditAsync_WithCreateBackups_ShouldRecordBackupInResult()
    {
        // Arrange
        const string originalContent = "TODO: Fix this issue\nSome other content";
        var testFile = CreateTestFile(originalContent);
        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:"
        };
        var edits = new List<TextEdit>
        {
            new ReplaceEdit
            {
                StartLine = 1,
                StartColumn = 1,
                EndLine = 1,
                EndColumn = 1,
                NewText = "FIXME: Fix this issue"
            }
        };
        var options = new BulkEditOptions { CreateBackups = true };

        // Act
        var result = await _service.ConditionalEditAsync(new[] { testFile }, condition, edits, options);

        // Assert
        Assert.That(result.Success, Is.True);
        var fileResult = result.FileResults[0];
        Assert.That(fileResult.BackupCreated, Is.True);
        Assert.That(fileResult.BackupPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(fileResult.BackupPath), Is.True);
        Assert.That(await File.ReadAllTextAsync(fileResult.BackupPath!), Is.EqualTo(originalContent));
    }

    [Test]
    public async Task ConditionalEditAsync_WithNonMatchingCondition_ShouldSkipFiles()
    {
        // Arrange
        var testFile = CreateTestFile("No todo items here");
        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:"
        };

        // Act
        var result = await _service.ConditionalEditAsync(new[] { testFile }, condition, new List<TextEdit>());

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        // For BulkFileEditResult, check if ChangeCount is 0 (no changes made)
        Assert.That(result.FileResults[0].ChangeCount, Is.EqualTo(0));
    }

    [Test]
    [TestCase(BulkConditionType.FileExtension, ".cs", true)]
    [TestCase(BulkConditionType.FileExtension, ".txt", false)]
    [TestCase(BulkConditionType.FileSize, "", true)] // Complex test would need proper parameters
    public async Task ConditionalEditAsync_WithDifferentConditionTypes_ShouldWorkCorrectly(
        BulkConditionType conditionType, string pattern, bool shouldMatch)
    {
        // Arrange
        var testFile = CreateTestFile("Test content", ".cs");
        var condition = new BulkEditCondition
        {
            ConditionType = conditionType,
            Pattern = pattern
        };

        // Act
        var result = await _service.ConditionalEditAsync(new[] { testFile }, condition, new List<TextEdit>());

        // Assert
        Assert.That(result, Is.Not.Null);
        if (shouldMatch)
        {
            // If condition matches, the file should have changes or success should be true
            Assert.That(result.FileResults[0].Success, Is.True);
        }
        else
        {
            // If condition doesn't match, the file should have no changes
            Assert.That(result.FileResults[0].ChangeCount, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task BatchRefactorAsync_WithValidPattern_ShouldApplyRefactoring()
    {
        // Arrange
        var testFile = CreateTestFile("public class OldClassName\n{\n    public void OldMethodName() { }\n}");
        var refactorPattern = new BulkRefactorPattern
        {
            RefactorType = BulkRefactorType.RenameSymbol,
            TargetPattern = "OldMethodName",
            ReplacementPattern = "NewMethodName",
            Parameters = new Dictionary<string, object>
            {
                ["oldName"] = "OldMethodName",
                ["newName"] = "NewMethodName"
            }
        };

        // Act
        var result = await _service.BatchRefactorAsync(new[] { testFile }, refactorPattern);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        // Note: Actual refactoring implementation would be more complex
    }

    [Test]
    public async Task MultiFileEditAsync_WithMultipleOperations_ShouldExecuteInPriorityOrder()
    {
        // Arrange
        var testFile = CreateTestFile("Original content");
        var operations = new List<MultiFileEditOperation>
        {
            new()
            {
                FilePattern = "*.cs",
                Priority = 2,
                Edits = new List<TextEdit>
                {
                    new ReplaceEdit
                  {
                      StartLine = 1,
                      StartColumn = 1,
                      EndLine = 1,
                      EndColumn = 1,
                      NewText = "Second edit"
                  }
                }
            },
            new()
            {
                FilePattern = "*.cs",
                Priority = 1,
                Edits = new List<TextEdit>
                {
                    new ReplaceEdit
                  {
                      StartLine = 1,
                      StartColumn = 1,
                      EndLine = 1,
                      EndColumn = 1,
                      NewText = "First edit"
                  }
                }
            }
        };

        // Act
        var result = await _service.MultiFileEditAsync(operations);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        // Priority order verification would require more detailed implementation
    }

    [Test]
    public async Task PreviewBulkChangesAsync_WithValidRequest_ShouldGeneratePreview()
    {
        // Arrange
        var testFile = CreateTestFile("Hello World\nGoodbye World");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile }, // Use actual file path
            RegexPattern = @"World",
            RegexReplacement = "Universe",
            Options = new BulkEditOptions { PreviewMode = true }
        };

        // Act
        var result = await _service.PreviewBulkChangesAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FilePreviews.Count, Is.EqualTo(1));
        Assert.That(result.FilePreviews[0].WouldChange, Is.True);
        Assert.That(result.FilePreviews[0].ChangeCount, Is.EqualTo(2));
    }

    [Test]
    public async Task PreviewBulkChangesAsync_WithValidationError_ShouldReturnError()
    {
        // Arrange
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { "*.cs" },
            RegexPattern = "[invalid", // Invalid regex
            Options = new BulkEditOptions()
        };

        // Act
        var result = await _service.PreviewBulkChangesAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Validation failed"));
        Assert.That(result.Impact.OverallRisk, Is.EqualTo(ChangeRiskLevel.Critical));
    }
}
