using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services;
using MCPsharp.Services.AI;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Dispatch tests for the bulk operations hub, stream processing, Roslyn analyzer and extract_method tools.
/// </summary>
[TestFixture]
public class McpToolRegistryOperationToolsTests
{
    private const string CalculatorSource = @"using System;

public class Calculator
{
    public int Add(int a, int b)
    {
        var sum = a + b;
        Console.WriteLine(sum);
        return sum;
    }
}
";

    private static readonly string[] OutsideProjectFiles = { "../elsewhere.cs" };
    private static readonly string[] TestAnalyzerIds = { "Test" };

    private string _projectPath = null!;
    private RoslynWorkspace _workspace = null!;
    private IRoslynAnalyzerService _analyzerService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_operations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);
        File.WriteAllText(Path.Combine(_projectPath, "Calculator.cs"), CalculatorSource);
        File.WriteAllText(Path.Combine(_projectPath, "data.txt"), "alpha\nbeta\ngamma\n");

        _workspace = new RoslynWorkspace();
        _analyzerService = Substitute.For<IRoslynAnalyzerService>();
        _registry = McpToolRegistryConsolidatedToolsTests.CreateRegistry(
            new ProjectContextManager(), _workspace, _analyzerService, Path.GetTempPath());

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
        _registry.ExecuteTool(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private async Task AssertSucceeds(string toolName, object arguments)
    {
        var result = await Execute(toolName, arguments);
        Assert.That(result.Success, Is.True, $"{toolName} failed: {result.Error}");
        Assert.That(result.Result, Is.Not.Null);
    }

    #region Bulk operations hub

    [Test]
    public async Task BulkTools_DispatchToBulkOperationsHub()
    {
        var operations = new[] { new { path = "data.txt", operation = "validate" } };

        await AssertSucceeds("execute_bulk_operation", new { operation_type = "file_operation", operations });
        await AssertSucceeds("preview_bulk_operation", new { operation_type = "file_operation", operations });
        await AssertSucceeds("manage_bulk_operation", new { action = "list" });
    }

    [Test]
    public async Task GetBulkProgress_UnknownOperation_ReportsNotFound()
    {
        var result = await Execute("get_bulk_progress", new { operation_id = "does-not-exist" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
    }

    [Test]
    public async Task ExecuteBulkOperation_FilesOutsideProject_AreRejected()
    {
        var result = await Execute("execute_bulk_operation", new
        {
            operation_type = "replace",
            files = OutsideProjectFiles,
            pattern = "a",
            replacement = "b"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside the project root"));
    }

    #endregion

    #region Stream processing

    [Test]
    public async Task StreamTools_DispatchToStreamProcessingController()
    {
        await AssertSucceeds("process_stream", new { processor_type = "line", input_path = "data.txt", output_path = "data.out.txt" });
        await AssertSucceeds("manage_stream", new { action = "list" });

        var monitor = await Execute("monitor_stream", new { operation_id = "does-not-exist" });
        Assert.That(monitor.Error, Is.Null.Or.Contain("not found"));
    }

    [Test]
    public async Task ManageStream_RegisterProcessor_IsRejected()
    {
        var result = await Execute("manage_stream", new { action = "register_processor" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not supported"));
    }

    #endregion

    #region Roslyn analyzers

    [Test]
    public async Task ListRoslynAnalyzers_DispatchesToAnalyzerService()
    {
        _analyzerService.GetLoadedAnalyzers().Returns(ImmutableArray<IAnalyzer>.Empty);

        await AssertSucceeds("list_roslyn_analyzers", new { });

        _analyzerService.Received(1).GetLoadedAnalyzers();
    }

    [Test]
    public async Task LoadRoslynAnalyzers_File_DispatchesToAnalyzerService()
    {
        var assemblyPath = Path.Combine(_projectPath, "Fake.Analyzers.dll");
        await File.WriteAllBytesAsync(assemblyPath, Array.Empty<byte>());
        _analyzerService.LoadAnalyzersAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<IAnalyzer>.Empty);

        await AssertSucceeds("load_roslyn_analyzers", new { path = "Fake.Analyzers.dll" });

        await _analyzerService.Received(1).LoadAnalyzersAsync(assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunRoslynAnalyzers_DispatchesToAnalyzerService()
    {
        _analyzerService.RunAnalyzersAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new AnalyzerRunResult { Success = true, AnalyzersRun = ImmutableArray.Create("Test") });

        await AssertSucceeds("run_roslyn_analyzers", new { target_path = "Calculator.cs", analyzer_ids = TestAnalyzerIds });

        await _analyzerService.Received(1).RunAnalyzersAsync(
            Path.Combine(_projectPath, "Calculator.cs"), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadRoslynAnalyzers_MissingPath_ReturnsError()
    {
        var result = await Execute("load_roslyn_analyzers", new { path = "missing.dll" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("does not exist"));
    }

    #endregion

    #region Extract method

    [Test]
    public async Task ExtractMethod_DispatchesToExtractMethodService()
    {
        await AssertSucceeds("extract_method", new
        {
            file_path = "Calculator.cs",
            start_line = 7,
            end_line = 8,
            method_name = "ComputeAndPrint"
        });
    }

    [Test]
    public async Task ExtractMethod_InvalidRange_ReturnsServiceError()
    {
        var result = await Execute("extract_method", new { file_path = "Calculator.cs", start_line = 500, end_line = 501 });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Empty);
    }

    #endregion

    #region AI provider name

    [Test]
    public void CodebaseQueryService_ProviderName_ComesFromProvider()
    {
        var provider = Substitute.For<IAIProvider>();
        provider.ProviderName.Returns("TestProvider");

        var withProvider = new CodebaseQueryService(provider, new ProjectContextManager(), NullLogger<CodebaseQueryService>.Instance);
        var withoutProvider = new CodebaseQueryService(null, new ProjectContextManager(), NullLogger<CodebaseQueryService>.Instance);

        Assert.That(withProvider.ProviderName, Is.EqualTo("TestProvider"));
        Assert.That(withoutProvider.ProviderName, Is.Null);
    }

    #endregion
}
