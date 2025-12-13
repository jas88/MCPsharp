using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

public class StaticMethodAnalyzerTests
{
    private static async Task<(Document document, Compilation compilation)> CreateTestDocument(string code)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(documentId, "Test.cs", SourceText.From(code));

        var document = solution.GetDocument(documentId)!;
        var compilation = await document.Project.GetCompilationAsync();

        return (document, compilation!);
    }

    private static async Task<List<Diagnostic>> GetDiagnostics(Document document, Compilation compilation)
    {
        var analyzer = new StaticMethodAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id == StaticMethodAnalyzer.DiagnosticId).ToList();
    }

    private static async Task<Document> ApplyCodeFix(Document document, Diagnostic diagnostic)
    {
        var fixer = new StaticMethodFixer();
        var actions = new List<CodeAction>();

        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await fixer.RegisterCodeFixesAsync(context);

        if (actions.Count == 0)
            return document;

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var operation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

        if (operation == null)
            return document;

        var newSolution = operation.ChangedSolution;
        return newSolution.GetDocument(document.Id)!;
    }

    [Test]
    public async Task NoDuplicateStaticModifier_WhenAppliedTwice()
    {
        // Arrange
        var code = @"
class TestClass
{
    private int Add(int a, int b)
    {
        return a + b;
    }
}";

        var (document, compilation) = await CreateTestDocument(code);

        // Act - Apply fix once
        var diagnostics = await GetDiagnostics(document, compilation);
        Assert.That(diagnostics, Has.Count.EqualTo(1));

        document = await ApplyCodeFix(document, diagnostics[0]);
        var text = (await document.GetTextAsync()).ToString();

        // Verify first application
        Assert.That(text, Does.Contain("private static"));
        Assert.That(text, Does.Not.Contain("static static"));

        // Act - Try to apply again (shouldn't produce duplicate)
        compilation = await document.Project.GetCompilationAsync();
        diagnostics = await GetDiagnostics(document, compilation!);

        // Should be no diagnostics since method is already static
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task DoesNotSuggestStatic_WhenMethodAccessesInstanceMember()
    {
        // Arrange
        var code = @"
class TestClass
{
    private int _value = 10;

    private int GetValuePlusTen()
    {
        return _value + 10;
    }
}";

        var (document, compilation) = await CreateTestDocument(code);

        // Act
        var diagnostics = await GetDiagnostics(document, compilation);

        // Assert - Should not suggest making it static since it accesses _value
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task DoesNotSuggestStatic_WhenMethodUsedAsDelegate()
    {
        // Arrange
        var code = @"
using System;

class TestClass
{
    private void Execute()
    {
        Action action = HelperMethod;
        action();
    }

    private void HelperMethod()
    {
        Console.WriteLine(""Hello"");
    }
}";

        var (document, compilation) = await CreateTestDocument(code);

        // Act
        var diagnostics = await GetDiagnostics(document, compilation);

        // Assert - Should not suggest making HelperMethod static since it's used as a delegate
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task SuggestsStatic_WhenMethodDoesNotAccessInstanceMembers()
    {
        // Arrange
        var code = @"
class TestClass
{
    private int Add(int a, int b)
    {
        return a + b;
    }
}";

        var (document, compilation) = await CreateTestDocument(code);

        // Act
        var diagnostics = await GetDiagnostics(document, compilation);

        // Assert
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo(StaticMethodAnalyzer.DiagnosticId));
    }

    [Test]
    public async Task ProperlyFormatsStaticModifier_WithMultipleModifiers()
    {
        // Arrange
        var code = @"
class TestClass
{
    private async Task<int> CalculateAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }
}";

        var (document, compilation) = await CreateTestDocument(code);

        // Act
        var diagnostics = await GetDiagnostics(document, compilation);
        Assert.That(diagnostics, Has.Count.EqualTo(1));

        document = await ApplyCodeFix(document, diagnostics[0]);
        var text = (await document.GetTextAsync()).ToString();

        // Assert - Should be "private static async" with proper spacing
        Assert.That(text, Does.Contain("private static async"));
        Assert.That(text, Does.Not.Contain("static static"));
        Assert.That(text, Does.Not.Contain("  static")); // No double spaces before static
        Assert.That(text, Does.Not.Contain("static  ")); // No double spaces after static
    }
}
