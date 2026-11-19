using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Analyzer IDs name the analyzer type and its assembly, so they are unique across assemblies and stable across runs
/// </summary>
public partial class RoslynAnalyzerAdapterTests
{
    private const string SameNameAnalyzer = "SameNameAnalyzer";

    [Test]
    public void Id_NamesTheAnalyzerTypeAndTheNameAndVersionOfItsAssembly()
    {
        var adapter = new RoslynAnalyzerAdapter(new TestDiagnosticAnalyzer(), _mockLogger);

        var assembly = typeof(TestDiagnosticAnalyzer).Assembly.GetName();
        Assert.That(adapter.Id, Is.EqualTo($"Roslyn_{typeof(TestDiagnosticAnalyzer).FullName}@{assembly.Name}-{assembly.Version}"));
        Assert.That(adapter.VersionIndependentId, Is.EqualTo($"Roslyn_{typeof(TestDiagnosticAnalyzer).FullName}@{assembly.Name}"));
        Assert.That(RoslynAnalyzerAdapter.GetId(typeof(TestDiagnosticAnalyzer)), Is.EqualTo(adapter.Id));
    }

    [Test]
    public void Id_IsTheSameForEveryInstanceOfAnAnalyzer()
    {
        var first = new RoslynAnalyzerAdapter(new TestDiagnosticAnalyzer(), _mockLogger);
        var second = new RoslynAnalyzerAdapter(new TestDiagnosticAnalyzer(), _mockLogger);

        Assert.That(second.Id, Is.EqualTo(first.Id));
    }

    [Test]
    public async Task Id_OfSameNamedAnalyzersInDifferentAssemblies_Differ()
    {
        var first = await LoadEmittedAnalyzerAsync($"ProbeA_{Guid.NewGuid():N}", "1.0.0.0");
        var second = await LoadEmittedAnalyzerAsync($"ProbeB_{Guid.NewGuid():N}", "1.0.0.0");

        Assert.That(second.Name, Is.EqualTo(first.Name));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public async Task Id_OfOneAnalyzerInTwoVersionsOfItsAssembly_Differ()
    {
        // e.g. the SDK's and a NuGet package's builds of the code style analyzers
        var assemblyName = $"ProbeVersions_{Guid.NewGuid():N}";
        var older = await LoadEmittedAnalyzerAsync(assemblyName, "1.0.0.0");
        var newer = await LoadEmittedAnalyzerAsync(assemblyName, "2.0.0.0");

        Assert.That(newer.Id, Is.Not.EqualTo(older.Id));
        Assert.That(newer.Id, Does.EndWith("-2.0.0.0"));
        Assert.That(((RoslynAnalyzerAdapter)newer).VersionIndependentId, Is.EqualTo(((RoslynAnalyzerAdapter)older).VersionIndependentId));
    }

    /// <summary>
    /// Compile <see cref="SameNameAnalyzer"/> into <paramref name="assemblyName"/> in a directory of its own,
    /// so each build loads into its own analyzer load context, and load it
    /// </summary>
    private async Task<IAnalyzer> LoadEmittedAnalyzerAsync(string assemblyName, string assemblyVersion)
    {
        var path = Path.Combine(CreateTestDirectory(Guid.NewGuid().ToString("N")), assemblyName + ".dll");
        AnalyzerAssemblyEmitter.Emit(assemblyName, path, AnalyzerAssemblyEmitter.AnalyzerSource(SameNameAnalyzer, "PROBE002"), assemblyVersion);
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(path);

        Assert.That(analyzers, Has.Length.EqualTo(1));
        return analyzers[0];
    }
}
