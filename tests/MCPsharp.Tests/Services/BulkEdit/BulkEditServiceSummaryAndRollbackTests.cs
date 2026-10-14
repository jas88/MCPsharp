using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.BulkEdit;

/// <summary>
/// Verifies that every bulk edit operation reports summary statistics and records a rollback
/// session that can restore the original file contents.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("BulkEdit")]
public class BulkEditServiceSummaryAndRollbackTests : FileServiceTestBase
{
    private BulkEditService _service = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _service = new BulkEditService(CreateNullLogger<BulkEditService>());
    }

    [TearDown]
    protected override void TearDown()
    {
        _service?.Dispose();
        base.TearDown();
    }

    private static ReplaceEdit ReplaceFirstFourChars(string newText) => new()
    {
        StartLine = 0,
        StartColumn = 0,
        EndLine = 0,
        EndColumn = 4,
        NewText = newText
    };

    private static void AssertRollbackRecorded(BulkEditResult result, int expectedBackups)
    {
        var rollbackInfo = result.RollbackInfo
            ?? throw new AssertionException("RollbackInfo should be recorded when backups are enabled");
        Assert.That(result.RollbackSessionId, Is.EqualTo(rollbackInfo.RollbackId));
        Assert.That(rollbackInfo.Files, Has.Count.EqualTo(expectedBackups));
        Assert.That(rollbackInfo.Files, Has.All.Matches<RollbackFileInfo>(f => f is not null && f.BackupExists));
    }

    private async Task AssertRollbackRestoresAsync(BulkEditResult result, Dictionary<string, string> originals)
    {
        var rollbackId = result.RollbackInfo?.RollbackId
            ?? throw new AssertionException("RollbackInfo should be recorded when backups are enabled");
        var rollbackResult = await _service.RollbackBulkEditAsync(rollbackId);

        Assert.That(rollbackResult.Success, Is.True, rollbackResult.Error);
        Assert.That(rollbackResult.ModifiedFiles, Is.EqualTo(originals.Count));
        foreach (var (path, content) in originals)
        {
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(content), path);
        }

        var available = await _service.GetAvailableRollbacksAsync();
        Assert.That(available, Has.None.Matches<RollbackInfo>(r => r is not null && r.RollbackId == rollbackId));
    }

    [Test]
    public async Task ConditionalEditAsync_PopulatesSummaryAndRollbackRestoresFiles()
    {
        const string matchingContent = "TODO: fix this\nother line";
        const string otherContent = "nothing to see here";
        var matchingFile = CreateTestFile(matchingContent);
        var otherFile = CreateTestFile(otherContent);
        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:"
        };
        var options = new BulkEditOptions { CreateBackups = true };

        var result = await _service.ConditionalEditAsync(
            new[] { matchingFile, otherFile }, condition, new List<TextEdit> { ReplaceFirstFourChars("DONE") }, options);

        Assert.That(result.Success, Is.True, result.Error);
        var summary = result.SummaryData;
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.TotalFilesMatched, Is.EqualTo(2));
        Assert.That(summary.TotalFilesProcessed, Is.EqualTo(2));
        Assert.That(summary.SuccessfulFiles, Is.EqualTo(2));
        Assert.That(summary.FailedFiles, Is.EqualTo(0));
        Assert.That(summary.SkippedFiles, Is.EqualTo(1));
        Assert.That(summary.TotalChangesApplied, Is.EqualTo(1));
        Assert.That(summary.TotalBytesProcessed, Is.EqualTo(matchingContent.Length + otherContent.Length));
        Assert.That(summary.TotalBytesWritten, Is.EqualTo(matchingContent.Length + otherContent.Length));
        Assert.That(summary.BackupsCreated, Is.EqualTo(2));
        Assert.That(result.Summary, Is.EqualTo(summary.ToString()));
        Assert.That(await File.ReadAllTextAsync(matchingFile), Is.EqualTo("DONE: fix this\nother line"));

        AssertRollbackRecorded(result, expectedBackups: 2);
        await AssertRollbackRestoresAsync(result, new Dictionary<string, string>
        {
            [matchingFile] = matchingContent,
            [otherFile] = otherContent
        });
    }

    [Test]
    public async Task BatchRefactorAsync_PopulatesSummaryAndRollbackRestoresFiles()
    {
        const string refactoredContent = "class C { void OldName() { OldName(); } }";
        const string untouchedContent = "class D { }";
        var refactoredFile = CreateTestFile(refactoredContent);
        var untouchedFile = CreateTestFile(untouchedContent);
        var pattern = new BulkRefactorPattern
        {
            RefactorType = BulkRefactorType.RenameSymbol,
            TargetPattern = "OldName",
            ReplacementPattern = "NewName"
        };
        var options = new BulkEditOptions { CreateBackups = true };

        var result = await _service.BatchRefactorAsync(new[] { refactoredFile, untouchedFile }, pattern, options);

        Assert.That(result.Success, Is.True, result.Error);
        var summary = result.SummaryData;
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.TotalFilesMatched, Is.EqualTo(2));
        Assert.That(summary.TotalFilesProcessed, Is.EqualTo(2));
        Assert.That(summary.SuccessfulFiles, Is.EqualTo(2));
        Assert.That(summary.FailedFiles, Is.EqualTo(0));
        Assert.That(summary.SkippedFiles, Is.EqualTo(1));
        Assert.That(summary.TotalChangesApplied, Is.EqualTo(1));
        Assert.That(summary.TotalBytesProcessed, Is.EqualTo(refactoredContent.Length + untouchedContent.Length));
        Assert.That(summary.BackupsCreated, Is.EqualTo(2));
        Assert.That(await File.ReadAllTextAsync(refactoredFile), Is.EqualTo("class C { void NewName() { NewName(); } }"));

        AssertRollbackRecorded(result, expectedBackups: 2);
        await AssertRollbackRestoresAsync(result, new Dictionary<string, string>
        {
            [refactoredFile] = refactoredContent,
            [untouchedFile] = untouchedContent
        });
    }

    [Test]
    public async Task MultiFileEditAsync_PopulatesSummaryAndRollbackRestoresFiles()
    {
        const string firstContent = "AAAA first file";
        const string secondContent = "BBBB second file";
        var firstFile = CreateTestFile(firstContent);
        var secondFile = CreateTestFile(secondContent);
        var operations = new List<MultiFileEditOperation>
        {
            // Lower priority runs second, editing the result of the first operation
            new()
            {
                FilePattern = firstFile,
                Priority = 2,
                Edits = new List<TextEdit> { ReplaceFirstFourChars("ZZZZ") }
            },
            new()
            {
                FilePattern = firstFile,
                Priority = 1,
                Edits = new List<TextEdit> { ReplaceFirstFourChars("YYYY") }
            },
            new()
            {
                FilePattern = secondFile,
                Priority = 1,
                Edits = new List<TextEdit> { ReplaceFirstFourChars("XXXX") }
            }
        };
        var options = new BulkEditOptions { CreateBackups = true };

        var result = await _service.MultiFileEditAsync(operations, options);

        Assert.That(result.Success, Is.True, result.Error);
        var summary = result.SummaryData;
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.TotalFilesMatched, Is.EqualTo(2));
        Assert.That(summary.TotalFilesProcessed, Is.EqualTo(3));
        Assert.That(summary.SuccessfulFiles, Is.EqualTo(3));
        Assert.That(summary.FailedFiles, Is.EqualTo(0));
        Assert.That(summary.SkippedFiles, Is.EqualTo(0));
        Assert.That(summary.TotalChangesApplied, Is.EqualTo(3));
        Assert.That(summary.BackupsCreated, Is.EqualTo(2));
        Assert.That(await File.ReadAllTextAsync(firstFile), Is.EqualTo("ZZZZ first file"));
        Assert.That(await File.ReadAllTextAsync(secondFile), Is.EqualTo("XXXX second file"));

        // Backups must capture the content from before the first operation ran
        AssertRollbackRecorded(result, expectedBackups: 2);
        await AssertRollbackRestoresAsync(result, new Dictionary<string, string>
        {
            [firstFile] = firstContent,
            [secondFile] = secondContent
        });
    }

    [Test]
    public async Task ConditionalEditAsync_InPreviewMode_PopulatesSummaryWithoutRollbackSession()
    {
        const string content = "TODO: preview me";
        var file = CreateTestFile(content);
        var condition = new BulkEditCondition
        {
            ConditionType = BulkConditionType.FileContains,
            Pattern = "TODO:"
        };
        var options = new BulkEditOptions { CreateBackups = true, PreviewMode = true };

        var result = await _service.ConditionalEditAsync(
            new[] { file }, condition, new List<TextEdit> { ReplaceFirstFourChars("DONE") }, options);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.SummaryData, Is.Not.Null);
        Assert.That(result.SummaryData!.TotalChangesApplied, Is.EqualTo(1));
        Assert.That(result.SummaryData.BackupsCreated, Is.EqualTo(0));
        Assert.That(result.RollbackInfo, Is.Null);
        Assert.That(result.RollbackSessionId, Is.Empty);
        Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo(content));
    }

    [Test]
    public async Task BatchRefactorAsync_WithoutBackups_PopulatesSummaryWithoutRollbackSession()
    {
        var file = CreateTestFile("int OldName;");
        var pattern = new BulkRefactorPattern
        {
            RefactorType = BulkRefactorType.RenameSymbol,
            TargetPattern = "OldName",
            ReplacementPattern = "NewName"
        };
        var options = new BulkEditOptions { CreateBackups = false };

        var result = await _service.BatchRefactorAsync(new[] { file }, pattern, options);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.SummaryData, Is.Not.Null);
        Assert.That(result.SummaryData!.TotalChangesApplied, Is.EqualTo(1));
        Assert.That(result.SummaryData.BackupsCreated, Is.EqualTo(0));
        Assert.That(result.RollbackInfo, Is.Null);
        Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("int NewName;"));
    }
}
