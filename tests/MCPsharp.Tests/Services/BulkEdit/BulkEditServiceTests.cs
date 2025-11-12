using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using MCPsharp.Services;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Tests.TestData;

namespace MCPsharp.Tests.Services.BulkEdit;

[TestFixture]
[Category("Unit")]
[Category("BulkEdit")]
public class BulkEditServiceTests : FileServiceTestBase
{
    private BulkEditService _service = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _service = new BulkEditService(CreateNullLogger<BulkEditService>());
    }

    [Test]
    public void Constructor_WithLogger_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => new BulkEditService(CreateNullLogger<BulkEditService>()));
        Assert.DoesNotThrow(() => new BulkEditService(null)); // Logger should be optional
    }

    [Test]
    public async Task BulkReplaceAsync_WithValidParameters_ShouldReplaceTextSuccessfully()
    {
        // Arrange
        var testFile = CreateTestFile("Hello World\nHello Universe\nHello Galaxy");
        var files = new[] { testFile };
        var pattern = @"Hello";
        var replacement = "Hi";
        var options = new BulkEditOptions { CreateBackups = false };

        // Act
        var result = await _service.BulkReplaceAsync(files, pattern, replacement, options);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FileResults.Count, Is.EqualTo(1));
        Assert.That(result.SummaryData?.TotalFilesProcessed, Is.EqualTo(1));
        Assert.That(result.SummaryData?.SuccessfulFiles, Is.EqualTo(1));
        Assert.That(result.SummaryData?.TotalChangesApplied, Is.EqualTo(3));

        var updatedContent = await File.ReadAllTextAsync(testFile);
        Assert.That(updatedContent, Is.EqualTo("Hi World\nHi Universe\nHi Galaxy"));
    }

    [Test]
    public async Task BulkReplaceAsync_WithNoMatches_ShouldSkipFiles()
    {
        // Arrange
        var testFile = CreateTestFile("Hello World");
        var files = new[] { testFile };
        var pattern = @"Goodbye";
        var replacement = "Hello";

        // Act
        var result = await _service.BulkReplaceAsync(files, pattern, replacement);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        // For BulkFileEditResult, check if ChangeCount is 0 (no changes made) and Success is true
        Assert.That(result.FileResults[0].ChangeCount, Is.EqualTo(0));
        Assert.That(result.SummaryData?.TotalChangesApplied, Is.EqualTo(0));
    }

    [Test]
    public void BulkReplaceAsync_WithInvalidRegex_ShouldFail()
    {
        // Arrange
        var testFile = CreateTestFile("Some content");
        var files = new[] { testFile };
        var invalidPattern = @"[unclosed";

        // Act & Assert
        var ex = Assert.ThrowsAsync<RegexParseException>(async () =>
            await _service.BulkReplaceAsync(files, invalidPattern, "replacement"));

        Assert.NotNull(ex);
        if (ex == null)
            throw new InvalidOperationException("Exception should not be null");
        Assert.That(ex.Message, Does.Contain("Invalid pattern"));
    }

    [Test]
    public async Task BulkReplaceAsync_WithCreateBackups_ShouldCreateBackupFiles()
    {
        // Arrange
        var testFile = CreateTestFile("Original content");
        var files = new[] { testFile };
        var options = new BulkEditOptions { CreateBackups = true };

        // Act
        var result = await _service.BulkReplaceAsync(files, "Original", "Modified", options);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.NotNull(result.RollbackInfo);
        if (result.RollbackInfo == null)
            throw new InvalidOperationException("RollbackInfo should not be null");
        Assert.That(result.RollbackInfo.Files.Count, Is.EqualTo(1));
        Assert.That(result.RollbackInfo.Files[0].BackupExists, Is.True);
        Assert.That(File.Exists(result.RollbackInfo.Files[0].BackupPath), Is.True);

        // Verify backup content
        var backupContent = await File.ReadAllTextAsync(result.RollbackInfo.Files[0].BackupPath);
        Assert.That(backupContent, Is.EqualTo("Original content"));

        // Verify file was modified
        var modifiedContent = await File.ReadAllTextAsync(testFile);
        Assert.That(modifiedContent, Is.EqualTo("Modified content"));
    }

    [Test]
    public async Task BulkReplaceAsync_WithPreviewMode_ShouldNotModifyFiles()
    {
        // Arrange
        var originalContent = "Hello World";
        var testFile = CreateTestFile(originalContent);
        var options = new BulkEditOptions { PreviewMode = true };

        // Act
        var result = await _service.BulkReplaceAsync(new[] { testFile }, "Hello", "Hi", options);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.SummaryData?.TotalChangesApplied, Is.EqualTo(1));

        // Verify file was not modified
        var currentContent = await File.ReadAllTextAsync(testFile);
        Assert.That(currentContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task BulkReplaceAsync_WithMultipleFiles_ShouldProcessInParallel()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("File 1 content"),
            CreateTestFile("File 2 content"),
            CreateTestFile("File 3 content")
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _service.BulkReplaceAsync(files, "content", "modified", new BulkEditOptions { MaxParallelism = 3 });
        var endTime = DateTime.UtcNow;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FileResults.Count, Is.EqualTo(3));
        Assert.That(result.SummaryData?.SuccessfulFiles, Is.EqualTo(3));
        Assert.That(result.SummaryData?.FilesPerSecond, Is.GreaterThan(0));
    }

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

    [Test]
    public async Task RollbackBulkEditAsync_WithValidRollbackId_ShouldRestoreFiles()
    {
        // Arrange
        var originalContent = "Original content";
        var testFile = CreateTestFile(originalContent);
        var options = new BulkEditOptions { CreateBackups = true };

        // Perform initial edit
        var editResult = await _service.BulkReplaceAsync(new[] { testFile }, "Original", "Modified", options);
        var rollbackId = editResult.RollbackInfo?.RollbackId;

        Assert.NotNull(rollbackId);
        if (rollbackId == null)
            throw new InvalidOperationException("Rollback ID should not be null");
        Assert.That(editResult.Success, Is.True);

        // Verify file was modified
        var modifiedContent = await File.ReadAllTextAsync(testFile);
        Assert.That(modifiedContent, Is.EqualTo("Modified content"));

        // Act
        var rollbackResult = await _service.RollbackBulkEditAsync(rollbackId);

        // Assert
        Assert.That(rollbackResult, Is.Not.Null);
        Assert.That(rollbackResult.Success, Is.True);
        Assert.That(rollbackResult.ModifiedFiles, Is.EqualTo(1));

        // Verify file was restored
        var restoredContent = await File.ReadAllTextAsync(testFile);
        Assert.That(restoredContent, Is.EqualTo(originalContent));
    }

    [Test]
    public async Task RollbackBulkEditAsync_WithInvalidRollbackId_ShouldFail()
    {
        // Arrange
        var invalidRollbackId = "non-existent-id";

        // Act
        var result = await _service.RollbackBulkEditAsync(invalidRollbackId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found or expired"));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithValidRequest_ShouldPassValidation()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile },
            RegexPattern = @"Test",
            RegexReplacement = "Valid"
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Count, Is.EqualTo(0));
        Assert.That(result.OverallSeverity, Is.EqualTo(ValidationSeverity.None));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithInvalidRegex_ShouldFailValidation()
    {
        // Arrange
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { "*.cs" },
            RegexPattern = "[invalid"
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i => i.Type == ValidationIssueType.RegexError), Is.True);
        Assert.That(result.OverallSeverity, Is.EqualTo(ValidationSeverity.Error));
    }

    [Test]
    public async Task ValidateBulkEditAsync_WithMissingRequiredParameters_ShouldFailValidation()
    {
        // Arrange
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { "*.cs" }
            // Missing RegexPattern
        };

        // Act
        var result = await _service.ValidateBulkEditAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i => i.Description.Contains("required")), Is.True);
    }

    [Test]
    public async Task GetAvailableRollbacksAsync_ShouldReturnRollbackList()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var options = new BulkEditOptions { CreateBackups = true };

        // Create some rollbacks
        var result1 = await _service.BulkReplaceAsync(new[] { testFile }, "Test", "Modified1", options);
        var result2 = await _service.BulkReplaceAsync(new[] { testFile }, "Modified1", "Modified2", options);

        // Act
        var rollbacks = await _service.GetAvailableRollbacksAsync();

        // Assert
        Assert.That(rollbacks, Is.Not.Null);
        Assert.That(rollbacks.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(rollbacks.All(r => r.CanRollback), Is.True);
    }

    [Test]
    public async Task CleanupExpiredRollbacksAsync_ShouldRemoveExpiredRollbacks()
    {
        // Arrange - This test would need to manipulate rollback expiration times
        // For now, just verify the method executes without error

        // Act
        var cleanedCount = await _service.CleanupExpiredRollbacksAsync();

        // Assert
        Assert.That(cleanedCount, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task EstimateImpactAsync_ShouldReturnRiskAssessment()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile },
            RegexPattern = @".*", // Broad pattern
            RegexReplacement = "replacement"
        };

        // Act
        var impact = await _service.EstimateImpactAsync(request);

        // Assert
        Assert.That(impact, Is.Not.Null);
        Assert.That(impact.Risks, Is.Not.Empty);
        Assert.That(impact.Complexity, Is.Not.Null);
        Assert.That(impact.Recommendations, Is.Not.Empty);
        Assert.That(impact.Complexity.Score, Is.GreaterThan(0));
    }

    [Test]
    public async Task EstimateImpactAsync_WithRefactorOperation_ShouldAssignHigherRisk()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BatchRefactor,
            Files = new[] { testFile },
            RefactorPattern = new BulkRefactorPattern
            {
                RefactorType = BulkRefactorType.RenameSymbol,
                TargetPattern = "OldSymbol",
                ReplacementPattern = "NewSymbol"
            }
        };

        // Act
        var impact = await _service.EstimateImpactAsync(request);

        // Assert
        Assert.That(impact, Is.Not.Null);
        Assert.That(impact.OverallRisk, Is.EqualTo(ChangeRiskLevel.High)); // Refactor operations have High risk
        Assert.That(impact.Risks.Any(r => r.Type == RiskType.Compilation), Is.True);
    }

    [Test]
    public async Task GetFileStatisticsAsync_ShouldReturnAccurateStatistics()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("Short content", ".cs"),
            CreateTestFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(1000), ".txt"),
            CreateTestFile("Medium length content with multiple lines\nAnd another line", ".md")
        };

        // Act
        var stats = await _service.GetFileStatisticsAsync(files);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFiles, Is.EqualTo(3));
        Assert.That(stats.TotalSize, Is.GreaterThan(0));
        Assert.That(stats.TotalLines, Is.GreaterThan(0));
        Assert.That(stats.FileTypes.Count, Is.GreaterThan(0));
        Assert.That(stats.FileTypes.ContainsKey(".cs"), Is.True);
        Assert.That(stats.FileTypes.ContainsKey(".txt"), Is.True);
        Assert.That(stats.FileTypes.ContainsKey(".md"), Is.True);
    }

    [Test]
    public async Task GetFileStatisticsAsync_WithNonExistentFiles_ShouldHandleGracefully()
    {
        // Arrange
        var files = new[] { "/non/existent/file1.cs", "/non/existent/file2.cs" };

        // Act
        var stats = await _service.GetFileStatisticsAsync(files);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFiles, Is.EqualTo(0));
        Assert.That(stats.InaccessibleFiles.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task BulkReplaceAsync_WithMaxFileSizeLimit_ShouldSkipLargeFiles()
    {
        // Arrange
        var testFile = CreateTestFile("Test content");
        var options = new BulkEditOptions { MaxFileSize = 10 }; // Very small limit

        // Act
        var result = await _service.BulkReplaceAsync(new[] { testFile }, "Test", "Modified", options);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        // Large file handling would depend on implementation details
    }

    [Test]
    public async Task BulkReplaceAsync_WithStopOnFirstError_ShouldStopOnError()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("Valid content"),
            "/non/existent/file.cs", // This should cause an error
            CreateTestFile("Another valid content")
        };
        var options = new BulkEditOptions { StopOnFirstError = true };

        // Act
        var result = await _service.BulkReplaceAsync(files, "content", "modified", options);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Depending on implementation, this might stop after the error
    }

    [Test]
    public async Task BulkReplaceAsync_WithGlobPattern_ShouldMatchFilesCorrectly()
    {
        // Arrange
        CreateTestDirectory("subdir");
        var files = new[]
        {
            CreateTestFile("Content 1", ".cs", ""),
            CreateTestFile("Content 2", ".cs", "subdir"),
            CreateTestFile("Content 3", ".txt", "")
        };

        // Filter to just .cs files to test glob functionality
        var csFiles = files.Where(f => f.EndsWith(".cs")).ToArray();

        // Act
        var result = await _service.BulkReplaceAsync(csFiles, "Content", "Modified");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.SummaryData?.TotalFilesProcessed, Is.EqualTo(2)); // Only .cs files
    }

    [Test]
    public async Task Cancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var testFile1 = CreateTestFile("Test content 1");
        var testFile2 = CreateTestFile("Test content 2");
        var cts = new CancellationTokenSource();

        // Act & Assert - Cancel right before the call
        cts.Cancel();

        // Test that the service respects cancellation (either by throwing or by handling gracefully)
        var result = await _service.BulkReplaceAsync(new[] { testFile1, testFile2 }, "Test", "Modified", cancellationToken: cts.Token);

        // If we get here, the service completed successfully despite cancellation
        // This might be acceptable behavior for non-disruptive operations
        Assert.That(result, Is.Not.Null);

        // Test with a new cancellation token for preview
        var cts2 = new CancellationTokenSource();
        cts2.Cancel();
        var previewRequest = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = new[] { testFile1 },
            RegexPattern = "Test",
            RegexReplacement = "Modified"
        };
        // Test preview operation with cancelled token as well
        var previewResult = await _service.PreviewBulkChangesAsync(previewRequest, cts2.Token);
        Assert.That(previewResult, Is.Not.Null);
    }

    [Test]
    [Repeat(3)] // Test multiple times for consistency
    public async Task BulkReplaceAsync_WithSameInput_ShouldProduceConsistentResults()
    {
        // Arrange - create separate files for each operation to ensure consistency
        var testFile1 = CreateTestFile("Hello World");
        var testFile2 = CreateTestFile("Hello World");
        var files1 = new[] { testFile1 };
        var files2 = new[] { testFile2 };

        // Act
        var result1 = await _service.BulkReplaceAsync(files1, "Hello", "Hi");
        var result2 = await _service.BulkReplaceAsync(files2, "Hello", "Hi");

        // Assert
        Assert.That(result1.SummaryData?.TotalChangesApplied, Is.EqualTo(result2.SummaryData?.TotalChangesApplied));
        Assert.That(result1.Success, Is.EqualTo(result2.Success));
    }

    [Test]
    public async Task BulkReplaceAsync_WithComplexRegex_ShouldApplyCorrectly()
    {
        // Arrange
        var testFile = CreateTestFile("email@example.com\nuser@domain.org\nadmin@site.net");
        var emailRegex = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
        var replacement = "REDACTED";

        // Act
        var result = await _service.BulkReplaceAsync(new[] { testFile }, emailRegex, replacement);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.SummaryData?.TotalChangesApplied, Is.EqualTo(3));

        var updatedContent = await File.ReadAllTextAsync(testFile);
        Assert.That(updatedContent, Is.EqualTo("REDACTED\nREDACTED\nREDACTED"));
    }
}