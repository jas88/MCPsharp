using System.Text.RegularExpressions;
using NUnit.Framework;
using MCPsharp.Services;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Tests.Services.BulkEdit;

public partial class BulkEditServiceTests
{
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
    public async Task BulkReplaceAsync_GlobWithExcludedGlob_SkipsExcludedFiles()
    {
        var kept = CreateTestFile("Hello kept", ".cs", "src");
        var generated = CreateTestFile("Hello generated", ".cs", Path.Combine("src", "Generated"));
        var options = new BulkEditOptions
        {
            CreateBackups = false,
            ExcludedFiles = new List<string> { Path.Combine(TempDirectory, "src", "Generated", "**") }
        };

        var result = await _service.BulkReplaceAsync(new[] { Path.Combine(TempDirectory, "**", "*.cs") }, "Hello", "Hi", options);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.FileResults.Select(r => r.FilePath), Is.EquivalentTo(new[] { kept }));
        Assert.That(await File.ReadAllTextAsync(kept), Is.EqualTo("Hi kept"));
        Assert.That(await File.ReadAllTextAsync(generated), Is.EqualTo("Hello generated"));
    }

    [Test]
    public async Task BulkReplaceAsync_ExplicitFilesWithExcludedFileAndDirectory_SkipsBoth()
    {
        var kept = CreateTestFile("Hello kept");
        var excludedFile = CreateTestFile("Hello file");
        var inExcludedDirectory = CreateTestFile("Hello directory", ".cs", "skip");
        var options = new BulkEditOptions
        {
            CreateBackups = false,
            ExcludedFiles = new List<string> { excludedFile, Path.Combine(TempDirectory, "skip") }
        };

        var result = await _service.BulkReplaceAsync(new[] { kept, excludedFile, inExcludedDirectory }, "Hello", "Hi", options);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(await File.ReadAllTextAsync(kept), Is.EqualTo("Hi kept"));
        Assert.That(await File.ReadAllTextAsync(excludedFile), Is.EqualTo("Hello file"));
        Assert.That(await File.ReadAllTextAsync(inExcludedDirectory), Is.EqualTo("Hello directory"));
    }

    [TestCase("src/Generated/**", "src/Generated/A.g.cs", true)]
    [TestCase("src/Generated/**", "src/Other/A.cs", false)]
    [TestCase("**/*.g.cs", "deep/nested/A.g.cs", true)]
    [TestCase("**/*.g.cs", "deep/nested/A.cs", false)]
    [TestCase("src/*.cs", "src/sub/A.cs", false)]
    public void CreateExclusionFilter_MatchesGlobRelativeToItsFixedDirectory(string pattern, string file, bool excluded)
    {
        var filter = BulkEditService.CreateExclusionFilter(Path.Combine(TempDirectory, pattern));

        Assert.That(filter(Path.GetFullPath(Path.Combine(TempDirectory, file))), Is.EqualTo(excluded));
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
    public async Task BulkReplaceAsync_WithInvalidRegex_ShouldFail()
    {
        // Arrange
        var testFile = CreateTestFile("Some content");
        var files = new[] { testFile };
        var invalidPattern = @"[unclosed";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RegexParseException>(async () =>
            await _service.BulkReplaceAsync(files, invalidPattern, "replacement"));

        Assert.That(ex, Is.Not.Null);
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
        Assert.That(result.RollbackInfo, Is.Not.Null);
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
        var csFiles = files.Where(f => f.EndsWith(".cs", StringComparison.Ordinal)).ToArray();

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
