using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.AI;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// With every optional service supplied, the tools listed by tools/list must be exactly the tools
/// ExecuteTool can dispatch, and each listed tool must carry a usable schema. Path arguments of the
/// raw-path tools must be confined to the open project.
/// </summary>
[TestFixture]
public class McpToolRegistryToolCatalogTests
{
    private string _projectPath = null!;
    private ProjectContextManager _projectContext = null!;
    private RoslynWorkspace _workspace = null!;
    private BulkEditService _bulkEditService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_catalog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_projectPath, "docs"));
        File.WriteAllText(Path.Combine(_projectPath, "data.txt"), "alpha\nbeta\n");
        File.WriteAllText(Path.Combine(_projectPath, "docs", "notes.txt"), "alpha notes\n");

        _projectContext = new ProjectContextManager();
        _workspace = new RoslynWorkspace();
        _bulkEditService = new BulkEditService();
        _registry = CreateFullRegistry();
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        _bulkEditService.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    private McpToolRegistry CreateFullRegistry()
    {
        var progressTracker = new ProgressTrackerService(NullLogger<ProgressTrackerService>.Instance);
        var tempFileManager = new TempFileManagerService();
        var streamingProcessor = new StreamingFileProcessor(NullLogger<StreamingFileProcessor>.Instance, progressTracker, tempFileManager);
        var universalFileOps = new UniversalFileOperations(Path.GetTempPath(), new FileOperationsService(Path.GetTempPath()), _bulkEditService);
        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.ProviderName.Returns("TestProvider");

        return new McpToolRegistry(
            _projectContext,
            _workspace,
            bulkEditService: _bulkEditService,
            streamingProcessor: streamingProcessor,
            progressTracker: progressTracker,
            tempFileManager: tempFileManager,
            sqlMigrationAnalyzer: new SqlMigrationAnalyzerService(NullLogger<SqlMigrationAnalyzerService>.Instance, NullLoggerFactory.Instance),
            universalFileOps: universalFileOps,
            unifiedAnalysis: new UnifiedAnalysisService(workspace: _workspace),
            bulkOperationsHub: new BulkOperationsHub(_bulkEditService, universalFileOps),
            streamController: new StreamProcessingController(streamingProcessor, progressTracker, tempFileManager),
            roslynAnalyzerService: Substitute.For<IRoslynAnalyzerService>(),
            codebaseQuery: new CodebaseQueryService(aiProvider, _projectContext, NullLogger<CodebaseQueryService>.Instance),
            aiCodeTransformation: new AICodeTransformationService(aiProvider, _workspace, NullLogger<AICodeTransformationService>.Instance));
    }

    private async Task<ToolCallResult> Execute(string toolName, object arguments) =>
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private async Task OpenProjectAsync()
    {
        var result = await Execute("project_open", new { path = _projectPath });
        Assert.That(result.Success, Is.True, result.Error);
    }

    #region Catalogue

    [Test]
    public void EveryDispatchedToolIsListed_AndEveryListedToolIsDispatched()
    {
        var listed = _registry.GetTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var dispatched = _registry.HandledToolNames.ToHashSet(StringComparer.Ordinal);

        Assert.That(dispatched.Except(listed).Order(StringComparer.Ordinal), Is.Empty, "Dispatched but not listed");
        Assert.That(listed.Except(dispatched).Order(StringComparer.Ordinal), Is.Empty, "Listed but not dispatched");
        Assert.That(_registry.GetTools().Select(t => t.Name), Is.Unique);
    }

    [Test]
    public void ListedTools_WithoutOptionalServices_AreStillAllDispatched()
    {
        var bare = new McpToolRegistry(new ProjectContextManager());

        Assert.That(bare.GetTools().Select(t => t.Name), Is.SubsetOf(bare.HandledToolNames));
    }

    [Test]
    public async Task UnlistedToolName_IsReportedAsUnknown()
    {
        var result = await Execute("no_such_tool", new { });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("Unknown tool: no_such_tool"));
    }

    [Test]
    public void EveryListedTool_HasDescriptionAndObjectSchemaDeclaringItsRequiredProperties()
    {
        foreach (var tool in _registry.GetTools())
        {
            Assert.That(tool.Description, Is.Not.Empty, tool.Name);
            var schema = tool.InputSchema.RootElement;
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"), tool.Name);
            var properties = schema.GetProperty("properties");
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                {
                    Assert.That(properties.TryGetProperty(name, out _), Is.True, $"{tool.Name}: required '{name}' not declared");
                }
            }

            foreach (var property in properties.EnumerateObject())
            {
                var type = property.Value.GetProperty("type").GetString();
                Assert.That(type, Is.AnyOf("string", "integer", "number", "boolean", "array", "object"), $"{tool.Name}.{property.Name}");
                if (type == "array")
                {
                    Assert.That(property.Value.TryGetProperty("items", out _), Is.True, $"{tool.Name}.{property.Name}: array without items");
                }
            }
        }
    }

    #endregion

    #region Project path confinement

    private static readonly string[] OutsideFiles = { "../outside.txt" };
    private static readonly string[] TraversingGlob = { "../*.txt" };
    private static readonly string[] ProjectGlob = { "**/*.txt" };
    private static readonly string[] ProjectFiles = { "data.txt", "docs/notes.txt" };

    [Test]
    public async Task BulkTools_PathsOutsideProject_AreRejected()
    {
        await OpenProjectAsync();

        var outside = await Execute("bulk_replace", new { files = OutsideFiles, regexPattern = "a", replacement = "b" });
        var traversal = await Execute("batch_refactor", new
        {
            files = TraversingGlob,
            refactorType = "RenameSymbol",
            targetPattern = "a",
            replacementPattern = "b"
        });

        Assert.That(outside.Success, Is.False);
        Assert.That(outside.Error, Does.Contain("outside the project root"));
        Assert.That(traversal.Success, Is.False);
        Assert.That(traversal.Error, Does.Contain("'..'"));
    }

    [Test]
    public async Task BulkReplace_FilesRelativeToProject_AreEdited()
    {
        await OpenProjectAsync();

        var result = await Execute("bulk_replace", new
        {
            files = ProjectFiles,
            regexPattern = "alpha",
            replacement = "omega",
            createBackups = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Is.EqualTo("omega\nbeta\n"));
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "docs", "notes.txt")), Is.EqualTo("omega notes\n"));
    }

    private static readonly string[] DocsExclusion = { "docs/**" };
    private static readonly string[] NotesFileExclusion = { "docs/notes.txt" };

    [Test]
    public async Task BulkReplace_GlobWithExcludedGlob_SkipsExcludedFiles()
    {
        await OpenProjectAsync();

        var result = await Execute("bulk_replace", new
        {
            files = ProjectGlob,
            excludedFiles = DocsExclusion,
            regexPattern = "alpha",
            replacement = "omega",
            createBackups = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Is.EqualTo("omega\nbeta\n"));
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "docs", "notes.txt")), Is.EqualTo("alpha notes\n"));
    }

    [Test]
    public async Task BulkReplace_ExplicitFilesWithExcludedPath_SkipsExcludedFile()
    {
        await OpenProjectAsync();

        var result = await Execute("bulk_replace", new
        {
            files = ProjectFiles,
            excludedFiles = NotesFileExclusion,
            regexPattern = "alpha",
            replacement = "omega",
            createBackups = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Is.EqualTo("omega\nbeta\n"));
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "docs", "notes.txt")), Is.EqualTo("alpha notes\n"));
    }

    [Test]
    public async Task BulkReplace_ExclusionOutsideProject_IsRejected()
    {
        await OpenProjectAsync();

        var result = await Execute("bulk_replace", new
        {
            files = ProjectFiles,
            excludedFiles = TraversingGlob,
            regexPattern = "alpha",
            replacement = "omega",
            createBackups = false
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("'..'"));
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Is.EqualTo("alpha\nbeta\n"));
    }

    [Test]
    public async Task ConditionalEdit_GlobRelativeToProject_EditsMatchingProjectFiles()
    {
        await OpenProjectAsync();

        var result = await Execute("conditional_edit", new
        {
            files = ProjectGlob,
            conditionType = "FileContains",
            pattern = "notes",
            edits = new[] { new { type = "insert", newText = "// ", startLine = 0, startColumn = 0 } }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "docs", "notes.txt")), Is.EqualTo("// alpha notes\n"));
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Is.EqualTo("alpha\nbeta\n"));
    }

    [Test]
    public async Task MultiFileEdit_CamelCaseOperations_AreApplied()
    {
        await OpenProjectAsync();

        var result = await Execute("multi_file_edit", new
        {
            operations = new[]
            {
                new
                {
                    filePattern = "data.txt",
                    edits = new[] { new { type = "insert", newText = "first\n", startLine = 0, startColumn = 0 } }
                }
            }
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "data.txt")), Does.StartWith("first\nalpha"));
    }

    [Test]
    public async Task RawPathTools_PathsOutsideProject_AreRejected()
    {
        await OpenProjectAsync();
        var outsideFile = Path.Combine(Path.GetTempPath(), $"mcpsharp_outside_{Guid.NewGuid():N}.txt");

        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("stream_process_file", new { filePath = "data.txt", outputPath = outsideFile, processorType = "LineProcessor" }),
                     ("estimate_stream_processing", new { filePath = "../data.txt", processorType = "LineProcessor" }),
                     ("generate_analysis_report", new { format = "json", output_path = outsideFile }),
                     ("apply_roslyn_fixes", new { diagnostic_ids = new[] { "CA1822" }, target_path = "../x.cs" }),
                     ("analyze_code_smells", new { filePath = "../x.cs" }),
                     ("analyze_migrations", new { projectPath = Path.GetTempPath() }),
                     ("detect_duplicates", new { projectPath = ".." })
                 })
        {
            var result = await Execute(tool, args);
            Assert.That(result.Success, Is.False, tool);
            Assert.That(result.Error, Does.Contain("outside the project root"), tool);
        }

        Assert.That(File.Exists(outsideFile), Is.False);
    }

    [Test]
    public async Task RawPathTools_PathArgumentsWithoutOpenProject_AreRejected()
    {
        var result = await Execute("bulk_replace", new { files = ProjectGlob, regexPattern = "a", replacement = "b" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("No project is open"));
    }

    [TestCase("data.txt", false, true)]
    [TestCase("docs/notes.txt", false, true)]
    [TestCase("../data.txt", false, false)]
    [TestCase("**/*.cs", true, true)]
    [TestCase("docs/*.txt", true, true)]
    [TestCase("docs/../../*.txt", true, false)]
    [TestCase("*.txt", false, true)]
    public void TryResolveToolPath_ConfinesPathsAndGlobsToRoot(string path, bool allowGlob, bool accepted)
    {
        var error = McpToolRegistry.TryResolveToolPath(_projectPath, path, allowGlob, "files", out var resolved);

        Assert.That(error == null, Is.EqualTo(accepted), error);
        if (accepted)
        {
            Assert.That(Path.IsPathRooted(resolved), Is.True);
            Assert.That(resolved, Does.StartWith(_projectPath + Path.DirectorySeparatorChar));
        }
    }

    [Test]
    public void TryResolveToolPath_AbsoluteGlobInSiblingDirectory_IsRejected()
    {
        var siblingGlob = _projectPath + "2" + Path.DirectorySeparatorChar + "*.cs";

        Assert.That(McpToolRegistry.TryResolveToolPath(_projectPath, siblingGlob, true, "files", out _), Is.Not.Null);
    }

    #endregion
}
