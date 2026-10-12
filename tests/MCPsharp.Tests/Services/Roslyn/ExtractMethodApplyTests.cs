using MCPsharp.Models.Refactoring;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// Extractions that rewrite the file (non-preview mode): disk + workspace update, compilation
/// check, rollback session.
/// </summary>
[TestFixture]
public class ExtractMethodApplyTests
{
    private const string CalculatorSource = """
        namespace Demo;

        public class Calculator
        {
            private int _offset = 5;

            public int Compute(int input)
            {
                var doubled = input * 2;
                var total = doubled + _offset;
                return total * 3;
            }
        }

        """;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private string _projectPath = null!;
    private RoslynWorkspace _workspace = null!;
    private BulkEditService _bulkEdit = null!;
    private ExtractMethodService _service = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), "ExtractMethodApply_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectPath);

        _workspace = new RoslynWorkspace();
        await _workspace.InitializeAsync(_projectPath);
        _bulkEdit = new BulkEditService();
        _service = new ExtractMethodService(
            _workspace,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExtractMethodService>.Instance,
            _bulkEdit);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        _bulkEdit.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private async Task<string> CreateFileAsync(string name, string content)
    {
        var path = Path.Combine(_projectPath, name);
        // Raw string literals pick up the checkout's line endings (CRLF on Windows); tests expect LF
        await File.WriteAllTextAsync(path, Lf(content));
        await _workspace.AddDocumentAsync(path);
        return path;
    }

    private Task<ExtractMethodResult> ExtractAsync(
        string path, int startLine, int endLine, string? name = null, bool preview = false,
        bool? makeStatic = null, int? startColumn = null, int? endColumn = null) =>
        _service.ExtractMethodAsync(new ExtractMethodRequest
        {
            FilePath = path,
            StartLine = startLine,
            EndLine = endLine,
            StartColumn = startColumn,
            EndColumn = endColumn,
            MethodName = name,
            MakeStatic = makeStatic,
            Preview = preview
        });

    /// <summary>
    /// The workspace copy of the file must match the disk and compile without errors.
    /// </summary>
    private async Task AssertCompilesAsync(string path)
    {
        var document = _workspace.GetDocument(path);
        Assert.That(document, Is.Not.Null);
        var text = await document!.GetTextAsync();
        Assert.That(text.ToString(), Is.EqualTo(await File.ReadAllTextAsync(path)), "workspace and disk differ");

        var model = await document.GetSemanticModelAsync();
        var errors = model!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
    }

    [Test]
    public async Task Apply_StatementsWithParameterAndReturnValue_RewritesFileAndCompiles()
    {
        var path = await CreateFileAsync("Calculator.cs", CalculatorSource);

        var result = await ExtractAsync(path, 9, 10, "ComputeTotal");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Applied, Is.True);
        Assert.That(result.FilePath, Is.EqualTo(path));
        Assert.That(result.RollbackId, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Extraction!.Method.Signature, Is.EqualTo("private int ComputeTotal(int input)"));
        Assert.That(result.Extraction.CallSite.Code, Is.EqualTo("var total = ComputeTotal(input);"));
        Assert.That(result.Extraction.Characteristics.IsStatic, Is.False, "uses the _offset field");

        var expected = """
            namespace Demo;

            public class Calculator
            {
                private int _offset = 5;

                public int Compute(int input)
                {
                    var total = ComputeTotal(input);
                    return total * 3;
                }

                private int ComputeTotal(int input)
                {
                    var doubled = input * 2;
                    var total = doubled + _offset;
                    return total;
                }
            }

            """;
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(Lf(expected)));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Preview_DoesNotWriteFile()
    {
        var path = await CreateFileAsync("Calculator.cs", CalculatorSource);

        var result = await ExtractAsync(path, 9, 10, "ComputeTotal", preview: true);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Applied, Is.False);
        Assert.That(result.RollbackId, Is.Null);
        Assert.That(result.Preview, Is.Not.Null);
        Assert.That(result.Preview!.ModifiedCode, Does.Contain("var total = ComputeTotal(input);"));
        Assert.That(result.Preview.ModifiedCode, Does.Contain("private int ComputeTotal(int input)"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(Lf(CalculatorSource)));
        Assert.That(await _bulkEdit.GetAvailableRollbacksAsync(), Is.Empty);
    }

    [Test]
    public async Task Rollback_RestoresOriginalFile()
    {
        var path = await CreateFileAsync("Calculator.cs", CalculatorSource);

        var result = await ExtractAsync(path, 9, 10, "ComputeTotal");
        Assert.That(result.Applied, Is.True, result.Error?.Message);
        Assert.That(await File.ReadAllTextAsync(path), Is.Not.EqualTo(Lf(CalculatorSource)));

        var rollback = await _bulkEdit.RollbackBulkEditAsync(result.RollbackId!);

        Assert.That(rollback.Success, Is.True, rollback.Error);
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(Lf(CalculatorSource)));

        // The next extraction resynchronises the workspace with the restored file
        var again = await ExtractAsync(path, 9, 10, "ComputeTotal", preview: true);
        Assert.That(again.Success, Is.True, again.Error?.Message);
        Assert.That(again.Preview!.OriginalCode, Does.Contain("var doubled = input * 2;"));
    }

    [Test]
    public async Task Apply_ExtractionThatBreaksCompilation_IsRefusedWithoutWriting()
    {
        var path = await CreateFileAsync("Calculator.cs", CalculatorSource);

        // A static method cannot read the instance field _offset
        var result = await ExtractAsync(path, 9, 10, "ComputeTotal", makeStatic: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Applied, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("COMPILATION_ERRORS"));
        Assert.That(result.NewDiagnostics, Has.Some.Matches<ExtractionDiagnostic>(d => d!.Severity == "Error" && d.Id == "CS0120"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(Lf(CalculatorSource)));
        Assert.That(await _bulkEdit.GetAvailableRollbacksAsync(), Is.Empty);
    }

    [Test]
    public async Task Apply_RefParameterAndTupleResult_Compiles()
    {
        var path = await CreateFileAsync("Counter.cs", """
            public class Counter
            {
                public int Run(int start)
                {
                    int count = start;
                    count += 2;
                    var label = count.ToString();
                    var size = label.Length;
                    return count + size + label.Length;
                }
            }
            """);

        var result = await ExtractAsync(path, 6, 8, "Step");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.Parameters.Single().Modifier, Is.EqualTo("ref"));
        Assert.That(result.Extraction.CallSite.Code, Is.EqualTo("var (label, size) = Step(ref count);"));
        Assert.That(result.Extraction.Method.Signature, Is.EqualTo("private static (string label, int size) Step(ref int count)"));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_AsyncStatements_CreatesAwaitedAsyncMethod()
    {
        var path = await CreateFileAsync("Loader.cs", """
            using System.Threading.Tasks;

            public class Loader
            {
                public async Task<int> LoadAsync(int id)
                {
                    await Task.Delay(1);
                    var value = await Task.FromResult(id * 2);
                    return value + 1;
                }
            }
            """);

        var result = await ExtractAsync(path, 7, 8, "FetchAsync");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.Method.Signature, Is.EqualTo("private static async Task<int> FetchAsync(int id)"));
        Assert.That(result.Extraction.CallSite.Code, Is.EqualTo("var value = await FetchAsync(id);"));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_TrailingReturn_CallSiteReturnsResult()
    {
        var path = await CreateFileAsync("Grader.cs", """
            public class Grader
            {
                public string Grade(int score)
                {
                    var adjusted = score + 1;
                    if (adjusted > 90)
                    {
                        return "A";
                    }
                    return "B";
                }
            }
            """);

        var result = await ExtractAsync(path, 6, 10, "Classify");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.CallSite.Code, Is.EqualTo("return Classify(adjusted);"));
        Assert.That(result.Extraction.Method.Signature, Is.EqualTo("private static string Classify(int adjusted)"));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_EarlyReturnInsideSelection_IsRefused()
    {
        const string source = """
            public class Guard
            {
                public int Check(int value)
                {
                    if (value < 0)
                    {
                        return 0;
                    }
                    var doubled = value * 2;
                    return doubled;
                }
            }
            """;
        var path = await CreateFileAsync("Guard.cs", source);

        var result = await ExtractAsync(path, 5, 9);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("CONTROL_FLOW_EXITS"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(Lf(source)));
    }

    [Test]
    public async Task Apply_GenericMethod_CarriesTypeParameterAndConstraint()
    {
        var path = await CreateFileAsync("Picker.cs", """
            using System;

            public class Picker
            {
                public T Pick<T>(T first, T second) where T : IComparable<T>
                {
                    var best = first.CompareTo(second) >= 0 ? first : second;
                    return best;
                }
            }
            """);

        var result = await ExtractAsync(path, 7, 7, "Better");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.Characteristics.IsGeneric, Is.True);
        Assert.That(result.Extraction.Method.Signature, Is.EqualTo("private static T Better<T>(T first, T second) where T : IComparable<T>"));
        Assert.That(result.Extraction.CallSite.Code, Is.EqualTo("var best = Better<T>(first, second);"));
        Assert.That(await File.ReadAllTextAsync(path), Does.Not.Contain("\r"), "line breaks added by the formatter must match the file");
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_PreservesCrLfLineEndingsAndByteOrderMark()
    {
        // The constraint clause makes the formatter insert a line break of its own
        const string source = "public class Lines\r\n{\r\n    public string Run<T>(T x) where T : class\r\n    {\r\n        var y = x.ToString();\r\n        return y;\r\n    }\r\n}\r\n";
        var path = Path.Combine(_projectPath, "Lines.cs");
        await File.WriteAllTextAsync(path, source, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await _workspace.AddDocumentAsync(path);

        var result = await ExtractAsync(path, 5, 5, "Describe");

        Assert.That(result.Success, Is.True, result.Error?.Message);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.That(bytes.Take(3), Is.EqualTo(Utf8Bom));
        var text = await File.ReadAllTextAsync(path);
        Assert.That(text.Replace("\r\n", string.Empty, StringComparison.Ordinal), Does.Not.Contain("\n"), "all line breaks are CRLF");
        Assert.That(text, Does.Contain("var y = Describe<T>(x);\r\n"));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_Expression_ReplacedByCall()
    {
        var path = await CreateFileAsync("Area.cs", """
            public class Area
            {
                public double Circle(double radius)
                {
                    var area = 3.14159 * radius * radius;
                    return area;
                }
            }
            """);

        // "3.14159 * radius * radius" on line 5
        var line = (await File.ReadAllLinesAsync(path))[4];
        var start = line.IndexOf("3.14159", StringComparison.Ordinal) + 1;
        var end = line.IndexOf(';', StringComparison.Ordinal);

        var result = await ExtractAsync(path, 5, 5, "CircleArea", startColumn: start, endColumn: end);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.CallSite.Code, Is.EqualTo("CircleArea(radius)"));
        Assert.That(await File.ReadAllTextAsync(path), Does.Contain("var area = CircleArea(radius);"));
        Assert.That(await File.ReadAllTextAsync(path), Does.Contain("return 3.14159 * radius * radius;"));
        await AssertCompilesAsync(path);
    }

    [Test]
    public async Task Apply_DefaultName_AvoidsExistingMember()
    {
        var path = await CreateFileAsync("Names.cs", """
            public class Names
            {
                public int Run(int x)
                {
                    var y = x + 1;
                    return y;
                }

                private int ExtractedMethod() => 0;
            }
            """);

        var result = await ExtractAsync(path, 5, 5);

        Assert.That(result.Success, Is.True, result.Error?.Message);
        Assert.That(result.Extraction!.Method.Name, Is.EqualTo("ExtractedMethod1"));
        await AssertCompilesAsync(path);
    }
}
