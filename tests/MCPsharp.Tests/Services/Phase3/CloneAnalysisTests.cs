using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;

namespace MCPsharp.Tests.Services.Phase3;

/// <summary>
/// Unit tests of clone fingerprinting, alignment, diffing and extraction dependency analysis
/// </summary>
[TestFixture]
public class CloneAnalysisTests
{
    private static readonly (string, string)[] ExpectedTotalReturn = [("total", "int")];
    private static readonly string[] ExpectedParameterA = ["a"];
    private static readonly string[] ExpectedNonPublicMembers = ["Calculator._offset"];
    private static readonly string[] ExpectedReturnNames = ["total"];

    [Test]
    public void Fingerprint_RenamedIdentifiers_ProduceSameRenamedHash()
    {
        var (first, second) = TwoMethods(
            "int F(int x) { var limit = 10; return x + limit; }",
            "int G(int y) { var max = 10; return y + max; }");

        var fp1 = CloneFingerprintBuilder.Build(first);
        var fp2 = CloneFingerprintBuilder.Build(second);

        Assert.That(fp1.RenamedHash, Is.EqualTo(fp2.RenamedHash));
        Assert.That(CloneComparer.Classify(fp1, fp2), Is.EqualTo(CloneType.Renamed));
        Assert.That(CloneComparer.Align(fp1, fp2)!.SemanticSimilarity, Is.EqualTo(1.0));
    }

    [Test]
    public void Fingerprint_InconsistentRenaming_IsNotRenamedClone()
    {
        var (first, second) = TwoMethods(
            "int F(int a, int b) { return a + a; }",
            "int F(int a, int b) { return a + b; }");

        var fp1 = CloneFingerprintBuilder.Build(first);
        var fp2 = CloneFingerprintBuilder.Build(second);
        var alignment = CloneComparer.Align(fp1, fp2)!;

        Assert.That(CloneComparer.Classify(fp1, fp2), Is.EqualTo(CloneType.NearMiss));
        Assert.That(alignment.RenameConsistency, Is.LessThan(1.0));
        Assert.That(alignment.SemanticSimilarity, Is.LessThan(alignment.ShapeSimilarity));
    }

    [Test]
    public void Fingerprint_WithSemanticModel_ClassifiesTypeNames()
    {
        var (tree, model) = Compile("class Foo { } class C { void M() { Foo f = new Foo(); } }");
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var fingerprint = CloneFingerprintBuilder.Build(method, model);

        Assert.That(fingerprint.UsedSemanticModel, Is.True);
        Assert.That(fingerprint.Tokens.Where(t => t.Text == "Foo").Select(t => t.Kind), Is.All.EqualTo(NormalizedTokenKind.TypeName));
        Assert.That(fingerprint.Tokens.Single(t => t.Text == "f").Kind, Is.EqualTo(NormalizedTokenKind.Identifier));
    }

    [Test]
    public void Differences_ReportLiteralChangeAndIdentifierRenameWithLocations()
    {
        var (first, second) = TwoMethods(
            "int F(int x)\n{\n    var limit = 10;\n    return x + limit;\n}",
            "int F(int y)\n{\n    var limit = 20;\n    return y + limit;\n}");
        var fp1 = CloneFingerprintBuilder.Build(first);
        var fp2 = CloneFingerprintBuilder.Build(second);

        var differences = CloneDiffer.FindDifferences(fp1, fp2, CloneComparer.Align(fp1, fp2)!);

        var rename = differences.Single(d => d.Type == DifferenceType.Identifier);
        Assert.That(rename.Text1, Is.EqualTo("x"));
        Assert.That(rename.Text2, Is.EqualTo("y"));
        Assert.That(rename.Description, Does.Contain("2 occurrences"));
        Assert.That((rename.Location1.Line, rename.Location1.Column), Is.EqualTo((ClassLineOffset + 1, ClassIndent + 11)));

        var literal = differences.Single(d => d.Type == DifferenceType.Literal);
        Assert.That(literal.Text1, Is.EqualTo("10"));
        Assert.That(literal.Text2, Is.EqualTo("20"));
        Assert.That((literal.Location1.Line, literal.Location1.Column), Is.EqualTo((ClassLineOffset + 3, ClassIndent + 17)));
        Assert.That((literal.Location2.Line, literal.Location2.Column), Is.EqualTo((ClassLineOffset + 3, ClassIndent + 17)));

        Assert.That(differences, Has.Count.EqualTo(2));
    }

    [Test]
    public void Differences_ReportAddedStatementAlignedToStatementBoundary()
    {
        var (first, second) = TwoMethods(
            "void F() { a = 1; b = 2; }",
            "void F() { a = 1; c = 3; b = 2; }");
        var fp1 = CloneFingerprintBuilder.Build(first);
        var fp2 = CloneFingerprintBuilder.Build(second);

        var differences = CloneDiffer.FindDifferences(fp1, fp2, CloneComparer.Align(fp1, fp2)!);

        var added = differences.Where(d => d.Type == DifferenceType.StatementAdded).ToList();
        Assert.That(added, Has.Count.EqualTo(1));
        Assert.That(added[0].Text2, Is.EqualTo("c = 3;"));
        Assert.That(differences.Where(d => d.Type == DifferenceType.Structural), Is.Empty);
    }

    [Test]
    public void MyersDiff_RespectsEditDistanceBound()
    {
        string[] first = ["a", "b", "c", "d"];
        string[] second = ["a", "x", "c", "d"];

        Assert.That(MyersDiff.Compute(first, second, 1), Is.Null);
        var script = MyersDiff.Compute(first, second, 2);
        Assert.That(script, Is.Not.Null);
        Assert.That(script!.Count(op => op.Kind == DiffOperationKind.Equal), Is.EqualTo(3));
        Assert.That(MyersDiff.Similarity(first, second, 0.0, 100), Is.EqualTo(0.75));
    }

    [Test]
    public void Extraction_BlockAssigningOuterLocalReadLater_NeedsReturnValue()
    {
        var (blocks, model) = DependencyBlocks();

        var analysis = ExtractionDependencyAnalyzer.Analyze(blocks[0], model, "Calc.cs", 1);

        Assert.That(analysis.Mode, Is.EqualTo(ExtractionAnalysisMode.Semantic));
        Assert.That(analysis.ReturnValues.Select(v => (v.Name, v.Type)), Is.EqualTo(ExpectedTotalReturn));
        Assert.That(analysis.Parameters.Select(p => p.Name), Is.EqualTo(ExpectedParameterA));
        Assert.That(analysis.Warnings, Has.Some.Contains("must return"));
        Assert.That(analysis.SuggestedSignature, Is.EqualTo("static int ExtractedMethod(int a)"));
        Assert.That(analysis.IsSafe, Is.True);
    }

    [Test]
    public void Extraction_BlockContainingReturn_IsUnsafe()
    {
        var (blocks, model) = DependencyBlocks();

        var analysis = ExtractionDependencyAnalyzer.Analyze(blocks[1], model, "Calc.cs", 1);

        Assert.That(analysis.ContainsReturnStatement, Is.True);
        Assert.That(analysis.IsSafe, Is.False);
        Assert.That(analysis.UnsafeReasons, Has.Some.Contains("return statement"));
    }

    [Test]
    public void Extraction_PureBlock_IsSafe()
    {
        var (blocks, model) = DependencyBlocks();

        var analysis = ExtractionDependencyAnalyzer.Analyze(blocks[2], model, "Calc.cs", 1);

        Assert.That(analysis.IsSafe, Is.True);
        Assert.That(analysis.ReturnValues, Is.Empty);
        Assert.That(analysis.RequiresInstanceAccess, Is.False);
        Assert.That(analysis.ContainsReturnStatement, Is.False);
        Assert.That(analysis.SuggestedSignature, Is.EqualTo("static void ExtractedMethod(int a)"));
    }

    [Test]
    public void Extraction_BlockUsingInstanceField_RequiresInstanceAndReportsNonPublicMember()
    {
        var (blocks, model) = DependencyBlocks();

        var analysis = ExtractionDependencyAnalyzer.Analyze(blocks[3], model, "Calc.cs", 1);

        Assert.That(analysis.RequiresInstanceAccess, Is.True);
        Assert.That(analysis.ReferencedNonPublicMembers, Is.EqualTo(ExpectedNonPublicMembers));
        Assert.That(analysis.SuggestedSignature, Does.Not.StartWith("static"));
    }

    [Test]
    public void Extraction_SyntaxOnlyFallback_StillDetectsReturnValueAndReturnStatement()
    {
        var (blocks, _) = DependencyBlocks();

        var assigning = ExtractionDependencyAnalyzer.Analyze(blocks[0], null, "Calc.cs", 1);
        var returning = ExtractionDependencyAnalyzer.Analyze(blocks[1], null, "Calc.cs", 1);

        Assert.That(assigning.Mode, Is.EqualTo(ExtractionAnalysisMode.SyntaxOnly));
        Assert.That(assigning.ReturnValues.Select(v => v.Name), Is.EqualTo(ExpectedReturnNames));
        Assert.That(assigning.Parameters.Select(p => p.Name), Does.Contain("a"));
        Assert.That(returning.IsSafe, Is.False);
    }

    [Test]
    public void Extraction_ClassDeclaration_IsNotApplicable()
    {
        var (tree, model) = Compile("class C { }");
        var classNode = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        var analysis = ExtractionDependencyAnalyzer.Analyze(classNode, model, "C.cs", 1);

        Assert.That(analysis.Mode, Is.EqualTo(ExtractionAnalysisMode.NotApplicable));
        Assert.That(analysis.IsSafe, Is.False);
    }

    // Methods are wrapped in "class C\n{\n" with four-space indentation
    private const int ClassLineOffset = 2;
    private const int ClassIndent = 4;

    private static (MethodDeclarationSyntax First, MethodDeclarationSyntax Second) TwoMethods(string first, string second)
    {
        static MethodDeclarationSyntax Parse(string method)
        {
            var indented = string.Join("\n", method.Split('\n').Select(line => new string(' ', ClassIndent) + line));
            var tree = CSharpSyntaxTree.ParseText("class C\n{\n" + indented + "\n}\n");
            return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        }

        return (Parse(first), Parse(second));
    }

    private static (SyntaxTree Tree, SemanticModel Model) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Sample.cs");
        var compilation = CSharpCompilation.Create(
            "CloneAnalysisTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (tree, compilation.GetSemanticModel(tree));
    }

    private static (List<BlockSyntax> Blocks, SemanticModel Model) DependencyBlocks()
    {
        const string source = """
            class Calculator
            {
                private int _offset = 3;

                int Compute(int a)
                {
                    int total = 0;
                    {
                        total = a * 2;
                    }
                    {
                        if (a < 0)
                        {
                            return -1;
                        }
                        var ignored = a + 1;
                    }
                    {
                        var doubled = a * 2;
                        var squared = doubled * doubled;
                    }
                    {
                        var shifted = a + _offset;
                    }
                    return total;
                }
            }
            """;

        var (tree, model) = Compile(source);
        var body = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
        return (body.Statements.OfType<BlockSyntax>().ToList(), model);
    }
}
