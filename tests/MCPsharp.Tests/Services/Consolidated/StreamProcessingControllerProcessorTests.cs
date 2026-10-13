using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// End-to-end runs of the built-in processors through <see cref="StreamProcessingController"/>
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class StreamProcessingControllerProcessorTests
{
    private static readonly string[] OriginalInputNames = ["data.txt", "more.txt"];

    private string _tempDir = null!;
    private StreamProcessingController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mcpsharp-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _controller = new StreamProcessingController();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task ProcessFile_WithRegexProcessor_WritesTransformedOutput()
    {
        var input = WriteInput("in.txt", "version=1.2\nname=demo\nversion=3.4\n");
        var output = Path.Combine(_tempDir, "out.txt");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.File,
            ProcessorType = "regex",
            InputPath = input,
            OutputPath = output,
            ProcessorOptions = new ProcessorOptions
            {
                Parameters = new Dictionary<string, object> { ["Pattern"] = @"^version=", ["Replacement"] = "v=", ["Multiline"] = true }
            }
        });

        Assert.That(response.Success, Is.True, response.Error ?? response.Results?[0].Error);
        Assert.That(await File.ReadAllTextAsync(output), Is.EqualTo(Lines("v=1.2", "name=demo", "v=3.4")));
        Assert.That(response.Summary!.TotalRecordsProcessed, Is.EqualTo(3));
        Assert.That(response.Summary.TotalBytesProcessed, Is.EqualTo(new FileInfo(input).Length));
    }

    [Test]
    public async Task ProcessFile_WithLogProcessor_FiltersByLevel()
    {
        var input = WriteInput("app.log", "INFO ok\nERROR bad\n  detail\nINFO fine\n");
        var output = Path.Combine(_tempDir, "errors.log");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.File,
            ProcessorType = "log",
            InputPath = input,
            OutputPath = output,
            ProcessorOptions = new ProcessorOptions
            {
                Parameters = new Dictionary<string, object> { ["MinLevel"] = "error" }
            }
        });

        Assert.That(response.Success, Is.True, response.Error ?? response.Results?[0].Error);
        Assert.That(await File.ReadAllTextAsync(output), Is.EqualTo("ERROR bad\n  detail\n"));
    }

    [Test]
    public async Task ProcessDirectory_WithCsvProcessor_WritesOneOutputPerFile()
    {
        var inputDir = Path.Combine(_tempDir, "in");
        Directory.CreateDirectory(inputDir);
        await File.WriteAllTextAsync(Path.Combine(inputDir, "a.csv"), "x,y\n1,2\n");
        await File.WriteAllTextAsync(Path.Combine(inputDir, "b.csv"), "x,y\n3,4\n");
        var outputDir = Path.Combine(_tempDir, "out");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.Directory,
            ProcessorType = "csv",
            InputPath = inputDir,
            OutputPath = outputDir,
            SearchPattern = "*.csv",
            ProcessorOptions = new ProcessorOptions
            {
                Parameters = new Dictionary<string, object> { ["SelectedColumnIndexes"] = new List<object> { 1 } }
            }
        });

        Assert.That(response.Success, Is.True, response.Error ?? response.Results?.FirstOrDefault(r => !r.Success)?.Error);
        Assert.That(response.Summary!.SuccessfulFiles, Is.EqualTo(2));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "a_processed.csv")), Is.EqualTo(Lines("y", "2")));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "b_processed.csv")), Is.EqualTo(Lines("y", "4")));
    }

    [Test]
    public async Task ProcessDirectory_SameFileNameInSubdirectories_WritesSeparateOutputs()
    {
        var inputDir = Path.Combine(_tempDir, "in");
        WriteFile(Path.Combine(inputDir, "one", "data.txt"), "first\n");
        WriteFile(Path.Combine(inputDir, "two", "data.txt"), "second\n");
        var outputDir = Path.Combine(_tempDir, "out");

        var response = await _controller.ProcessStreamAsync(PrefixRequest(StreamInputType.Directory, inputDir, outputDir));

        Assert.That(response.Success, Is.True, response.Error ?? response.Results?.FirstOrDefault(r => !r.Success)?.Error);
        Assert.That(response.Summary!.SuccessfulFiles, Is.EqualTo(2));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "one", "data_processed.txt")), Is.EqualTo(Lines("> first")));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "two", "data_processed.txt")), Is.EqualTo(Lines("> second")));
    }

    [Test]
    public async Task ProcessMultipleFiles_SameFileNameInDifferentDirectories_WritesSeparateOutputs()
    {
        var first = Path.Combine(_tempDir, "a", "data.txt");
        var second = Path.Combine(_tempDir, "b", "c", "data.txt");
        WriteFile(first, "first\n");
        WriteFile(second, "second\n");
        var outputDir = Path.Combine(_tempDir, "out");

        var request = PrefixRequest(StreamInputType.MultipleFiles, null, outputDir);
        request.Files = new List<string> { first, second };
        var response = await _controller.ProcessStreamAsync(request);

        Assert.That(response.Success, Is.True, response.Error ?? response.Results?.FirstOrDefault(r => !r.Success)?.Error);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "a", "data_processed.txt")), Is.EqualTo(Lines("> first")));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "b", "c", "data_processed.txt")), Is.EqualTo(Lines("> second")));
    }

    [TestCase("out")]
    [TestCase("")]
    public async Task ProcessDirectory_OutputInsideInput_DoesNotReprocessOutputs(string outputSubdirectory)
    {
        var inputDir = Path.Combine(_tempDir, "in");
        WriteFile(Path.Combine(inputDir, "data.txt"), "line\n");
        WriteFile(Path.Combine(inputDir, "sub", "more.txt"), "other\n");
        var outputDir = Path.Combine(inputDir, outputSubdirectory);

        for (var run = 1; run <= 2; run++)
        {
            var response = await _controller.ProcessStreamAsync(PrefixRequest(StreamInputType.Directory, inputDir, outputDir));

            Assert.That(response.Success, Is.True, response.Error ?? response.Results?.FirstOrDefault(r => !r.Success)?.Error);
            Assert.That(response.Results!.Select(r => Path.GetFileName(r.InputPath)), Is.EquivalentTo(OriginalInputNames), $"run {run}");
        }

        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "data_processed.txt")), Is.EqualTo(Lines("> line")));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(outputDir, "sub", "more_processed.txt")), Is.EqualTo(Lines("> other")));
        Assert.That(Directory.EnumerateFiles(inputDir, "*_processed_processed*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task ProcessFile_WithJsonProcessor_InvalidInput_ReportsFailure()
    {
        var input = WriteInput("bad.json", "{ \"unterminated\": ");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.File,
            ProcessorType = "json",
            InputPath = input
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.Results![0].Success, Is.False);
        Assert.That(response.Results[0].Error, Does.StartWith("Invalid JSON"));
    }

    [Test]
    public async Task ProcessFile_WithUnknownProcessor_IsRejected()
    {
        var input = WriteInput("data.txt", "content\n");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.File,
            ProcessorType = "transform",
            InputPath = input
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Unknown processor type: transform"));
    }

    private string WriteInput(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static StreamProcessRequest PrefixRequest(StreamInputType inputType, string? inputPath, string outputPath) => new()
    {
        InputType = inputType,
        ProcessorType = "line",
        InputPath = inputPath,
        OutputPath = outputPath,
        Recursive = true,
        ProcessorOptions = new ProcessorOptions
        {
            Parameters = new Dictionary<string, object> { ["LinePrefix"] = "> " }
        }
    };

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
