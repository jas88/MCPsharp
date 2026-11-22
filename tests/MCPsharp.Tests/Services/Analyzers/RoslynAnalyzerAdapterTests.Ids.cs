using System.Collections.Immutable;
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

    [Test]
    public async Task Id_OfOneAnalyzerInAssembliesOfOneNameAndVersionWithDifferentKeys_Differ()
    {
        // e.g. a package's signed build of an analyzer and an unsigned local build of the same version
        var assemblyName = $"ProbeKeys_{Guid.NewGuid():N}";
        var unsigned = (RoslynAnalyzerAdapter)await LoadEmittedAnalyzerAsync(assemblyName, "1.0.0.0");
        var signed = (RoslynAnalyzerAdapter)await LoadEmittedAnalyzerAsync(assemblyName, "1.0.0.0", AnalyzerAssemblyEmitter.CreatePublicKey());
        var otherKey = (RoslynAnalyzerAdapter)await LoadEmittedAnalyzerAsync(assemblyName, "1.0.0.0", AnalyzerAssemblyEmitter.CreatePublicKey());

        Assert.That(new[] { unsigned.Id, signed.Id, otherKey.Id }, Is.Unique);
        Assert.That(new[] { unsigned.VersionIndependentId, signed.VersionIndependentId, otherKey.VersionIndependentId }, Is.Unique);
        var token = signed.DiagnosticAnalyzer.GetType().Assembly.GetName().GetPublicKeyToken()!;
        Assert.That(signed.Id, Is.EqualTo($"Roslyn_{SameNameAnalyzer}@{assemblyName}+{Convert.ToHexStringLower(token)}-1.0.0.0"));
        Assert.That(unsigned.Id, Is.EqualTo($"Roslyn_{SameNameAnalyzer}@{assemblyName}-1.0.0.0"));
    }

    /// <summary>
    /// Compile <see cref="SameNameAnalyzer"/> into <paramref name="assemblyName"/> in a directory of its own,
    /// so each build loads into its own analyzer load context, public-signed with <paramref name="publicKey"/> when
    /// given, and load it
    /// </summary>
    private async Task<IAnalyzer> LoadEmittedAnalyzerAsync(string assemblyName, string assemblyVersion, ImmutableArray<byte> publicKey = default)
    {
        var path = Path.Combine(CreateTestDirectory(Guid.NewGuid().ToString("N")), assemblyName + ".dll");
        AnalyzerAssemblyEmitter.Emit(assemblyName, path, AnalyzerAssemblyEmitter.AnalyzerSource(SameNameAnalyzer, "PROBE002"), assemblyVersion, publicKey);
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(path);

        Assert.That(analyzers, Has.Length.EqualTo(1));
        return analyzers[0];
    }
}
