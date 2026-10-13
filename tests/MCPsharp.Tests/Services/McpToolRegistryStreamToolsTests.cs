using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// End-to-end checks of the stream processing tools: processor options passed as tool arguments must
/// reach the processors and change the output written to disk.
/// </summary>
[TestFixture]
[Category("Streaming")]
public class McpToolRegistryStreamToolsTests
{
    private static readonly int[] ReversedColumns = { 2, 0 };

    private string _projectPath = null!;
    private RoslynWorkspace _workspace = null!;
    private ProgressTrackerService _progressTracker = null!;
    private TempFileManagerService _tempFileManager = null!;
    private StreamingFileProcessor _streamingProcessor = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_streamtools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);

        _workspace = new RoslynWorkspace();
        _progressTracker = new ProgressTrackerService(NullLogger<ProgressTrackerService>.Instance);
        _tempFileManager = new TempFileManagerService();
        _streamingProcessor = new StreamingFileProcessor(NullLogger<StreamingFileProcessor>.Instance, _progressTracker, _tempFileManager);
        _registry = new McpToolRegistry(
            new ProjectContextManager(),
            _workspace,
            streamingProcessor: _streamingProcessor,
            progressTracker: _progressTracker,
            tempFileManager: _tempFileManager,
            streamController: new StreamProcessingController(_streamingProcessor, _progressTracker, _tempFileManager));

        var open = await Execute("project_open", new { path = _projectPath });
        Assert.That(open.Success, Is.True, open.Error);
    }

    [TearDown]
    public void TearDown()
    {
        _streamingProcessor.Dispose();
        _tempFileManager.Dispose();
        _progressTracker.Dispose();
        _workspace.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    private Task<ToolCallResult> Execute(string toolName, object arguments) =>
        _registry.ExecuteTool(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private string WriteProjectFile(string name, string content)
    {
        var path = Path.Combine(_projectPath, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public async Task StreamProcessFile_RegexOptionsFromJson_ReplaceEveryMatch()
    {
        WriteProjectFile("input.txt", "cat and Cat\nno match\n");

        var result = await Execute("stream_process_file", new
        {
            filePath = "input.txt",
            outputPath = "output.txt",
            processorType = "RegexProcessor",
            createCheckpoint = false,
            processorOptions = new { pattern = "cat", replacement = "dog", ignoreCase = true }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "output.txt")), Is.EqualTo("dog and dog\nno match\n"));
    }

    [Test]
    public async Task ProcessStream_RegexProcessorOptions_AreAppliedToOutput()
    {
        WriteProjectFile("input.txt", "id 1\nid 22\nnone\n");

        var result = await Execute("process_stream", new
        {
            processor_type = "regex",
            input_path = "input.txt",
            output_path = "output.txt",
            processor_options = new { pattern = @"(\d+)", replacement = "<$1>" }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "output.txt")),
            Is.EqualTo(string.Join(Environment.NewLine, "id <1>", "id <22>", "none") + Environment.NewLine));
    }

    [Test]
    public async Task ProcessStream_CsvProcessorOptionsWithChunkSize_AreAppliedToOutput()
    {
        WriteProjectFile("input.csv", "a;b;c\n1;2;3\n");

        var result = await Execute("process_stream", new
        {
            processor_type = "csv",
            input_path = "input.csv",
            output_path = "output.csv",
            chunk_size = 4,
            processor_options = new { Delimiter = ";", SelectedColumnIndexes = ReversedColumns }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "output.csv")),
            Is.EqualTo(string.Join(Environment.NewLine, "c;a", "3;1") + Environment.NewLine));
    }

    [Test]
    public async Task ProcessStream_NonObjectProcessorOptions_IsRejected()
    {
        WriteProjectFile("input.txt", "text\n");

        var result = await Execute("process_stream", new
        {
            processor_type = "regex",
            input_path = "input.txt",
            output_path = "output.txt",
            processor_options = "pattern=x"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("processor_options"));
    }
}
