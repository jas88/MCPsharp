using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
public class RoslynCodeFixApplierTests
{
    internal const string TwoStaticCandidatesSource = @"public class Calc
{
    private int Add(int a, int b)
    {
        return a + b;
    }

    private int Mul(int a, int b)
    {
        return a * b;
    }

    public int Use() => Add(1, 2) + Mul(3, 4);
}
";

    internal const string TwoStaticCandidatesFixed = @"public class Calc
{
    private static int Add(int a, int b)
    {
        return a + b;
    }

    private static int Mul(int a, int b)
    {
        return a * b;
    }

    public int Use() => Add(1, 2) + Mul(3, 4);
}
";

    internal const string MultiDeclaratorSource = @"public class C
{
    public int M()
    {
        int a = 1, b = 2;
        return a + b;
    }
}
";

    internal const string MultiDeclaratorFixed = @"public class C
{
    public int M()
    {
        int a = 1;
        int b = 2;
        return a + b;
    }
}
";

    private const string FilePath = "/virtual/Calc.cs";
    private static readonly int[] ExpectedLines = { 3, 8 };

    private AdhocWorkspace _workspace = null!;
    private RoslynCodeFixApplier _applier = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new AdhocWorkspace();
        _applier = new RoslynCodeFixApplier();
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    private static RoslynCodeFixAdapter Provider(Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider provider) =>
        new(provider, NullLogger<RoslynCodeFixAdapter>.Instance);

    private Task<CodeFixRunResult> RunAsync(
        string source, DiagnosticAnalyzer analyzer, RoslynCodeFixAdapter provider, string id,
        IReadOnlyList<CodeFixTarget>? targets = null)
    {
        var document = RoslynCodeFixApplier.CreateAdhocDocument(_workspace, FilePath, source);
        return _applier.ApplyAsync(
            document, ImmutableArray.Create(analyzer), new[] { provider }, new[] { id }, targets);
    }

    [Test]
    public async Task ApplyAsync_TwoFixesWhereFirstShiftsPositions_BothLandCorrectly()
    {
        var run = await RunAsync(TwoStaticCandidatesSource, new StaticMethodAnalyzer(), Provider(new StaticMethodFixer()),
            StaticMethodAnalyzer.DiagnosticId);

        Assert.That(run.Outcomes, Has.Count.EqualTo(2));
        Assert.That(run.Outcomes.Select(o => o.Status), Is.All.EqualTo(CodeFixOutcomeStatus.Applied));
        Assert.That(run.Outcomes.Select(o => o.Line), Is.EqualTo(ExpectedLines));
        Assert.That(run.ChangedDocuments, Has.Count.EqualTo(1));
        Assert.That(run.ChangedDocuments[0].FilePath, Is.EqualTo(FilePath));
        Assert.That(run.ChangedDocuments[0].OriginalText, Is.EqualTo(TwoStaticCandidatesSource));
        Assert.That(run.ChangedDocuments[0].NewText, Is.EqualTo(TwoStaticCandidatesFixed));
        Assert.That(run.ChangedDocuments[0].Diff, Does.Contain("-    private int Add(int a, int b)"));
        Assert.That(run.ChangedDocuments[0].Diff, Does.Contain("+    private static int Mul(int a, int b)"));
    }

    [Test]
    public async Task ApplyAsync_DiagnosticResolvedByEarlierFix_IsSkipped()
    {
        var run = await RunAsync(MultiDeclaratorSource, new MultiDeclaratorTestAnalyzer(), Provider(new MultiDeclaratorTestFixer()),
            MultiDeclaratorTestAnalyzer.DiagnosticId);

        Assert.That(run.Outcomes.Select(o => o.Status),
            Is.EqualTo(new[] { CodeFixOutcomeStatus.Applied, CodeFixOutcomeStatus.Skipped }));
        Assert.That(run.Outcomes[1].Reason, Does.Contain("no longer reported"));
        Assert.That(run.ChangedDocuments.Single().NewText, Is.EqualTo(MultiDeclaratorFixed));
    }

    [Test]
    public async Task ApplyAsync_TargetWithoutDiagnostic_IsSkippedAndOthersApplied()
    {
        var targets = new[]
        {
            new CodeFixTarget(StaticMethodAnalyzer.DiagnosticId, 1, 1),   // no diagnostic here
            new CodeFixTarget(StaticMethodAnalyzer.DiagnosticId, 8, 17),  // Mul
            new CodeFixTarget(StaticMethodAnalyzer.DiagnosticId, 99, 1)   // outside the document
        };

        var run = await RunAsync(TwoStaticCandidatesSource, new StaticMethodAnalyzer(), Provider(new StaticMethodFixer()),
            StaticMethodAnalyzer.DiagnosticId, targets);

        Assert.That(run.AppliedCount, Is.EqualTo(1));
        Assert.That(run.SkippedCount, Is.EqualTo(2));
        Assert.That(run.ChangedDocuments.Single().NewText, Does.Contain("private int Add("));
        Assert.That(run.ChangedDocuments.Single().NewText, Does.Contain("private static int Mul("));
    }

    [Test]
    public async Task ApplyAsync_ProviderThatCannotFix_ReportsFailure()
    {
        var run = await RunAsync(TwoStaticCandidatesSource, new StaticMethodAnalyzer(), Provider(new MultiDeclaratorTestFixer()),
            StaticMethodAnalyzer.DiagnosticId);

        Assert.That(run.Outcomes, Has.Count.EqualTo(2));
        Assert.That(run.Outcomes.Select(o => o.Status), Is.All.EqualTo(CodeFixOutcomeStatus.Failed));
        Assert.That(run.ChangedDocuments, Is.Empty);
    }
}
