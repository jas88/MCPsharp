using System.Collections.Immutable;
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

    [Test]
    public void LoadAnalyzersFromReferences_CancelledWhileTheLastReferenceLoads_ShouldThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var reference = new CancellingAnalyzerReference(cts, TestAnalyzerReferences.BuiltInAnalyzers());

        Assert.Throws<OperationCanceledException>(() => _loader.LoadAnalyzersFromReferences([reference], cts.Token));
    }

    /// <summary>
    /// A reference that cancels <paramref name="cts"/> as its analyzers load, as a cancellation that arrives during
    /// a slow load does
    /// </summary>
    private sealed class CancellingAnalyzerReference(CancellationTokenSource cts, AnalyzerReference loaded) : AnalyzerReference
    {
        public override string? FullPath => loaded.FullPath;

        public override string Display => loaded.Display;

        public override object Id => loaded.Id;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
        {
            cts.Cancel();
            return loaded.GetAnalyzers(language);
        }

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
        {
            cts.Cancel();
            return loaded.GetAnalyzersForAllLanguages();
        }
    }
}
