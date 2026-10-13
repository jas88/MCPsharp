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
}
