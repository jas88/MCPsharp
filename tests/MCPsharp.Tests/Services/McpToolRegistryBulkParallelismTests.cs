using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// The bulk tools' parallelism arguments, bulk_replace's maxParallelism and execute_bulk_operation's
/// max_parallelism, cap the bulk edit's files processed at once, and without them the cap is the processor count.
/// BulkEditServiceTests checks that the service keeps to the cap.
/// </summary>
[TestFixture]
public class McpToolRegistryBulkParallelismTests
{
    private static readonly string[] DataFiles = { "data.txt" };

    private string _projectPath = null!;
    private RoslynWorkspace _workspace = null!;
    private IBulkEditService _bulkEditService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_parallelism_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);
        File.WriteAllText(Path.Combine(_projectPath, "data.txt"), "alpha\n");

        _workspace = new RoslynWorkspace();
        _bulkEditService = Substitute.For<IBulkEditService>();
        _bulkEditService.BulkReplaceAsync(default!, default!, default!, default, default).ReturnsForAnyArgs(SucceededEdit());
        _registry = McpToolRegistryConsolidatedToolsTests.CreateRegistry(
            new ProjectContextManager(), _workspace, Substitute.For<IRoslynAnalyzerService>(), Path.GetTempPath(), _bulkEditService);

        var open = await Execute("project_open", new { path = _projectPath });
        Assert.That(open.Success, Is.True, open.Error);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    private Task<ToolCallResult> Execute(string toolName, object arguments) =>
        _registry.ExecuteToolAsync(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private static BulkEditResult SucceededEdit() => new()
    {
        Success = true,
        TotalFiles = 1,
        ModifiedFiles = 1,
        SkippedFiles = 0,
        FailedFiles = 0,
        FileResults = [],
        Errors = [],
        ElapsedTime = TimeSpan.Zero,
        OperationId = "parallelism-test",
        StartTime = DateTime.UtcNow,
        EndTime = DateTime.UtcNow
    };

    /// <summary>
    /// The options of each bulk replace the tool made
    /// </summary>
    private List<BulkEditOptions?> ReceivedReplaceOptions() =>
        _bulkEditService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IBulkEditService.BulkReplaceAsync))
            .Select(call => call.GetArguments()[3] as BulkEditOptions)
            .ToList();

    private Task<ToolCallResult> BulkReplace(int? maxParallelism) => maxParallelism is { } value
        ? Execute("bulk_replace", new { files = DataFiles, regexPattern = "alpha", replacement = "omega", maxParallelism = value })
        : Execute("bulk_replace", new { files = DataFiles, regexPattern = "alpha", replacement = "omega" });

    private Task<ToolCallResult> ExecuteBulkReplaceOperation(int? maxParallelism) => maxParallelism is { } value
        ? Execute("execute_bulk_operation", new { operation_type = "replace", files = DataFiles, pattern = "alpha", replacement = "omega", max_parallelism = value })
        : Execute("execute_bulk_operation", new { operation_type = "replace", files = DataFiles, pattern = "alpha", replacement = "omega" });

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    public async Task BulkReplace_WithMaxParallelism_CapsTheEditAtIt(int maxParallelism)
    {
        var result = await BulkReplace(maxParallelism);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(ReceivedReplaceOptions().Select(o => o?.MaxParallelism), Is.EqualTo(new int?[] { maxParallelism }));
    }

    [Test]
    public async Task BulkReplace_WithoutMaxParallelism_CapsTheEditAtTheProcessorCount()
    {
        var result = await BulkReplace(null);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(ReceivedReplaceOptions().Select(o => o?.MaxParallelism), Is.EqualTo(new int?[] { Environment.ProcessorCount }));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task BulkReplace_WithMaxParallelismBelowOne_IsRejected(int maxParallelism)
    {
        var result = await BulkReplace(maxParallelism);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("maxParallelism must be at least 1"));
        Assert.That(ReceivedReplaceOptions(), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    public async Task ExecuteBulkOperation_WithMaxParallelism_CapsTheEditAtIt(int maxParallelism)
    {
        var result = await ExecuteBulkReplaceOperation(maxParallelism);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(ReceivedReplaceOptions().Select(o => o?.MaxParallelism), Is.EqualTo(new int?[] { maxParallelism }));
    }

    [Test]
    public async Task ExecuteBulkOperation_WithoutMaxParallelism_CapsTheEditAtTheProcessorCount()
    {
        var result = await ExecuteBulkReplaceOperation(null);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(ReceivedReplaceOptions().Select(o => o?.MaxParallelism), Is.EqualTo(new int?[] { Environment.ProcessorCount }));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task ExecuteBulkOperation_WithMaxParallelismBelowOne_IsRejected(int maxParallelism)
    {
        var result = await ExecuteBulkReplaceOperation(maxParallelism);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("max_parallelism must be at least 1"));
        Assert.That(ReceivedReplaceOptions(), Is.Empty);
    }
}
