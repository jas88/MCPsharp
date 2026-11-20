using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.BulkEdit;

/// <summary>
/// MaxParallelism: the most of an operation's files processed at once, the processor count by default, and never
/// more than one file per processor
/// </summary>
public partial class BulkEditServiceTests
{
    private const int ParallelismFileCount = 24;

    // With two processors or more, a bulk edit should process at least two files at once
    private static readonly int LeastInParallel = Math.Min(2, Environment.ProcessorCount);

    private List<string> CreateParallelismFiles() =>
        Enumerable.Range(0, ParallelismFileCount).Select(i => CreateTestFile($"File {i} content")).ToList();

    private static List<TextEdit> InsertCommentEdits() =>
        [new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = "// edited\n" }];

    [Test]
    public async Task BulkReplaceAsync_WithMaxParallelism_ProcessesAtMostThatManyFilesAtOnce()
    {
        var files = CreateParallelismFiles();

        var result = await _service.BulkReplaceAsync(files, "content", "edited", new BulkEditOptions { MaxParallelism = 2, CreateBackups = false });

        Assert.That(result.ModifiedFiles, Is.EqualTo(ParallelismFileCount), result.Error);
        Assert.That(BulkEditConcurrency.MostFilesInProgress(result.FileResults), Is.InRange(LeastInParallel, 2));
    }

    [Test]
    public async Task BulkReplaceAsync_WithoutMaxParallelism_ProcessesUpToOneFilePerProcessor()
    {
        var files = CreateParallelismFiles();

        var result = await _service.BulkReplaceAsync(files, "content", "edited", new BulkEditOptions { CreateBackups = false });

        Assert.That(new BulkEditOptions().MaxParallelism, Is.EqualTo(Environment.ProcessorCount));
        Assert.That(result.ModifiedFiles, Is.EqualTo(ParallelismFileCount), result.Error);
        Assert.That(BulkEditConcurrency.MostFilesInProgress(result.FileResults), Is.InRange(LeastInParallel, Environment.ProcessorCount));
    }

    [Test]
    public async Task BulkReplaceAsync_WithMaxParallelismAboveProcessorCount_ProcessesAtMostOneFilePerProcessor()
    {
        var files = CreateParallelismFiles();
        var options = new BulkEditOptions { MaxParallelism = 4 * Environment.ProcessorCount, CreateBackups = false };

        var result = await _service.BulkReplaceAsync(files, "content", "edited", options);

        Assert.That(result.ModifiedFiles, Is.EqualTo(ParallelismFileCount), result.Error);
        Assert.That(BulkEditConcurrency.MostFilesInProgress(result.FileResults), Is.InRange(LeastInParallel, Environment.ProcessorCount));
    }

    [Test]
    public async Task ConditionalEditAsync_WithMaxParallelismOfOne_ProcessesOneFileAtATime()
    {
        var files = CreateParallelismFiles();
        var condition = new BulkEditCondition { ConditionType = BulkConditionType.FileContains, Pattern = "content" };

        var result = await _service.ConditionalEditAsync(files, condition, InsertCommentEdits(), new BulkEditOptions { MaxParallelism = 1, CreateBackups = false });

        Assert.That(result.ModifiedFiles, Is.EqualTo(ParallelismFileCount), result.Error);
        Assert.That(BulkEditConcurrency.MostFilesInProgress(result.FileResults), Is.EqualTo(1));
    }

    [Test]
    public async Task MultiFileEditAsync_WithMaxParallelismOfOne_ProcessesOneFileAtATime()
    {
        var files = CreateParallelismFiles();
        // One operation over the whole directory, so its files can be processed in parallel
        var operations = new List<MultiFileEditOperation> { new() { FilePattern = TempDirectory, Edits = InsertCommentEdits() } };

        var result = await _service.MultiFileEditAsync(operations, new BulkEditOptions { MaxParallelism = 1, CreateBackups = false });

        Assert.That(result.ModifiedFiles, Is.EqualTo(files.Count), result.Error);
        Assert.That(BulkEditConcurrency.MostFilesInProgress(result.FileResults), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task BulkReplaceAsync_WithMaxParallelismBelowOne_FailsWithoutEditing(int maxParallelism)
    {
        var file = CreateTestFile("some content");

        var result = await _service.BulkReplaceAsync([file], "content", "edited", new BulkEditOptions { MaxParallelism = maxParallelism });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("MaxParallelism must be at least 1"));
        Assert.That(File.ReadAllText(file), Is.EqualTo("some content"));
    }
}
