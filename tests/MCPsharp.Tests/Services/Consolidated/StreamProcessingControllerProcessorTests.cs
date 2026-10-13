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
    public async Task ProcessFile_WithTransformProcessor_ReportsNotSupported()
    {
        var input = WriteInput("data.txt", "content\n");

        var response = await _controller.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = StreamInputType.File,
            ProcessorType = "transform",
            InputPath = input
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.Summary!.FailedFiles, Is.EqualTo(1));
        Assert.That(response.Results![0].Error, Does.Contain("not supported"));
    }

    private string WriteInput(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
