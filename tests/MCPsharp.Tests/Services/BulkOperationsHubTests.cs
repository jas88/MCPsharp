using MCPsharp.Models;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Conditional bulk operations must apply their edits (through the bulk edit service) to the
/// files meeting every condition, honouring dry_run and create_backup.
/// </summary>
[TestFixture]
public class BulkOperationsHubTests
{
    private const string Header = "// reviewed\n";

    private string _root = null!;
    private string _todoFile = null!;
    private string _plainFile = null!;
    private string _todoText = null!;
    private BulkEditService _bulkEditService = null!;
    private BulkOperationsHub _hub = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcpsharp_bulkhub_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _todoFile = Path.Combine(_root, "Todo.cs");
        _plainFile = Path.Combine(_root, "Plain.cs");
        _todoText = Path.Combine(_root, "notes.txt");
        File.WriteAllText(_todoFile, "// TODO: tidy\npublic class Todo { }\n");
        File.WriteAllText(_plainFile, "public class Plain { }\n");
        File.WriteAllText(_todoText, "TODO list\n");

        _bulkEditService = new BulkEditService();
        _hub = new BulkOperationsHub(
            _bulkEditService,
            new UniversalFileOperations(_root, new FileOperationsService(_root)),
            tempFileManager: new TempFileManagerService());
    }

    [TearDown]
    public void TearDown()
    {
        _bulkEditService.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static List<TextEdit> InsertHeader() => new()
    {
        new InsertEdit { StartLine = 0, StartColumn = 0, EndLine = 0, EndColumn = 0, NewText = Header }
    };

    private BulkOperationRequest ConditionalRequest(BulkOperationOptions options, params EditCondition[] conditions) => new()
    {
        OperationType = BulkOperationType.Conditional,
        Files = new List<string> { _todoFile, _plainFile, _todoText },
        Conditions = conditions.ToList(),
        Edits = InsertHeader(),
        Options = options
    };

    private static EditCondition Condition(string type, string value, string? op = null) =>
        new() { Type = type, Value = value, Operator = op };

    [Test]
    public async Task Conditional_AppliesEditsOnlyToMatchingFiles()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions(), Condition("contains", "TODO")));

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Success, Is.True);
        Assert.That(File.ReadAllText(_todoFile), Does.StartWith(Header + "// TODO"));
        Assert.That(File.ReadAllText(_todoText), Does.StartWith(Header + "TODO list"));
        Assert.That(File.ReadAllText(_plainFile), Is.EqualTo("public class Plain { }\n"));

        var byFile = response.Results!.ToDictionary(r => r.FilePath);
        Assert.That(byFile[_todoFile].Changes, Is.EqualTo(1));
        Assert.That(byFile[_plainFile].Changes, Is.Zero);
        Assert.That(byFile[_plainFile].Message, Is.EqualTo("Condition not met"));
        Assert.That(response.Summary!.TotalChanges, Is.EqualTo(2));
    }

    [Test]
    public async Task Conditional_MultipleConditions_AllMustHold()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions(), Condition("contains", "TODO"), Condition("extension", ".cs")));

        Assert.That(response.Success, Is.True, response.Error);
        Assert.That(File.ReadAllText(_todoFile), Does.StartWith(Header));
        Assert.That(File.ReadAllText(_todoText), Is.EqualTo("TODO list\n"));
        Assert.That(File.ReadAllText(_plainFile), Is.EqualTo("public class Plain { }\n"));
        Assert.That(response.Results!.Single(r => r.FilePath == _todoText).Message, Is.EqualTo("Condition not met"));
    }

    [Test]
    public async Task Conditional_NegatedCondition_SelectsNonMatchingFiles()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions(), Condition("notcontains", "TODO")));

        Assert.That(response.Success, Is.True, response.Error);
        Assert.That(File.ReadAllText(_plainFile), Does.StartWith(Header));
        Assert.That(File.ReadAllText(_todoFile), Does.Not.StartWith(Header));

        var negatedByOperator = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions(), Condition("regex", @"class \w+", "not")));

        Assert.That(negatedByOperator.Success, Is.True, negatedByOperator.Error);
        Assert.That(File.ReadAllText(_todoText), Does.StartWith(Header));
    }

    [Test]
    public async Task Conditional_DryRun_ReportsChangesWithoutWriting()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions { DryRun = true }, Condition("contains", "TODO")));

        Assert.That(response.Success, Is.True, response.Error);
        Assert.That(response.Results!.Single(r => r.FilePath == _todoFile).Changes, Is.EqualTo(1));
        Assert.That(File.ReadAllText(_todoFile), Is.EqualTo("// TODO: tidy\npublic class Todo { }\n"));
        Assert.That(File.ReadAllText(_todoText), Is.EqualTo("TODO list\n"));
    }

    [Test]
    public async Task Conditional_CreateBackup_AppliesEditsAndCleansUp()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions { CreateBackup = true }, Condition("contains", "TODO")));

        Assert.That(response.Success, Is.True, response.Error);
        Assert.That(response.RolledBack, Is.False);
        Assert.That(File.ReadAllText(_todoFile), Does.StartWith(Header));
    }

    [Test]
    public async Task PreviewConditional_WithoutOptions_DoesNotWrite()
    {
        var response = await _hub.PreviewBulkOperationAsync(new BulkPreviewRequest
        {
            OperationType = BulkOperationType.Conditional,
            Files = new List<string> { _todoFile, _plainFile },
            Conditions = new List<EditCondition> { Condition("contains", "TODO") },
            Edits = InsertHeader()
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.PreviewResults!.Single(r => r.FilePath == _todoFile).Changes, Is.EqualTo(1));
        Assert.That(File.ReadAllText(_todoFile), Is.EqualTo("// TODO: tidy\npublic class Todo { }\n"));
    }

    [Test]
    public async Task Conditional_WithoutEdits_IsRejected()
    {
        var request = ConditionalRequest(new BulkOperationOptions(), Condition("contains", "TODO"));
        request.Edits = null;

        var response = await _hub.ExecuteBulkOperationAsync(request);

        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("edits"));
        Assert.That(File.ReadAllText(_todoFile), Does.Not.StartWith(Header));
    }

    [Test]
    public async Task Conditional_UnsupportedConditionType_ReturnsError()
    {
        var response = await _hub.ExecuteBulkOperationAsync(
            ConditionalRequest(new BulkOperationOptions(), Condition("phase_of_moon", "full")));

        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Unsupported condition type"));
        Assert.That(File.ReadAllText(_todoFile), Does.Not.StartWith(Header));
    }
}
