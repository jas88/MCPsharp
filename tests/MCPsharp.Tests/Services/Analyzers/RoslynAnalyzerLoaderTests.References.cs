using MCPsharp.Services.Analyzers;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// LoadAnalyzersFromReferences: the C# analyzers of the given analyzer references, and nothing else
/// </summary>
public partial class RoslynAnalyzerLoaderTests
{
    [Test]
    public void LoadAnalyzersFromReferences_LoadsTheAnalyzersOfTheReferences()
    {
        var analyzers = _loader.LoadAnalyzersFromReferences([TestAnalyzerReferences.BuiltInAnalyzers()]);

        Assert.That(analyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(analyzers, Is.All.InstanceOf<RoslynAnalyzerAdapter>());
    }

    [Test]
    public void LoadAnalyzersFromReferences_WithNoReferences_LoadsNothing()
    {
        Assert.That(_loader.LoadAnalyzersFromReferences([]), Is.Empty);
    }

    [Test]
    public void LoadAnalyzersFromReferences_WithReferenceWithoutAnalyzers_LoadsNothing()
    {
        Assert.That(_loader.LoadAnalyzersFromReferences([TestAnalyzerReferences.BuiltInCodeFixes()]), Is.Empty);
    }

    [Test]
    public void LoadAnalyzersFromReferences_WithUnloadableReferences_SkipsThem()
    {
        var missing = TestAnalyzerReferences.Create(Path.Combine(TempDirectory, "Missing.Analyzers.dll"));
        var invalid = TestAnalyzerReferences.Create(CreateTestFile("not an assembly", ".dll"));

        var analyzers = _loader.LoadAnalyzersFromReferences([missing, invalid, TestAnalyzerReferences.BuiltInAnalyzers()]);

        Assert.That(analyzers.Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public void LoadAnalyzersFromReferences_ReusesTheReferencesAnalyzerInstances()
    {
        // A workspace has already loaded its references' analyzers; loading them again creates nothing new
        var reference = TestAnalyzerReferences.BuiltInAnalyzers();

        var first = _loader.LoadAnalyzersFromReferences([reference]).Cast<RoslynAnalyzerAdapter>().Select(a => a.DiagnosticAnalyzer).ToList();
        var second = _loader.LoadAnalyzersFromReferences([reference]).Cast<RoslynAnalyzerAdapter>().Select(a => a.DiagnosticAnalyzer).ToList();

        Assert.That(second, Is.EqualTo(first).Using<DiagnosticAnalyzer>(ReferenceEqualityComparer.Instance));
    }

    [Test]
    public void LoadAnalyzersFromReferences_WithCancellation_ShouldThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _loader.LoadAnalyzersFromReferences([TestAnalyzerReferences.BuiltInAnalyzers()], cts.Token));
    }
}
