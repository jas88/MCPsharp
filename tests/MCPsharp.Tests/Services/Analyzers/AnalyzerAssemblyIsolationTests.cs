using System.Runtime.Loader;
using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Analyzer assemblies loaded by the analyzer and code fix loaders must stay out of the default load context:
/// Roslyn resolves analyzers and source generators from the host's context by name first, so a copy loaded there
/// (e.g. the roslyn3.11 build of a generator found in the NuGet cache) replaces the build a project references.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public class AnalyzerAssemblyIsolationTests : FileServiceTestBase
{
    private string _assemblyName = null!;
    private string _assemblyPath = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _assemblyName = $"IsolationProbe_{Guid.NewGuid():N}";
        // Loaded analyzer assemblies stay locked on Windows; the base class ignores cleanup failures
        var directory = CreateTestDirectory("analyzers");
        _assemblyPath = Path.Combine(directory, _assemblyName + ".dll");
        AnalyzerAssemblyEmitter.Emit(_assemblyName, _assemblyPath, AnalyzerAssemblyEmitter.AnalyzerSource("IsolationProbeAnalyzer", "PROBE001"));
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_LoadsOutsideDefaultContext()
    {
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(_assemblyPath);

        Assert.That(analyzers, Has.Length.EqualTo(1), "analyzer types must be assignable to the host's DiagnosticAnalyzer");
        AssertNotInDefaultContext();
    }

    [Test]
    public void DiscoverAnalyzerAssemblies_InspectsOutsideDefaultContext()
    {
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        var discovered = loader.DiscoverAnalyzerAssemblies(Path.GetDirectoryName(_assemblyPath)!);

        Assert.That(discovered, Does.Contain(_assemblyPath));
        AssertNotInDefaultContext();
    }

    [Test]
    public void DiscoverCodeFixProviderAssemblies_InspectsOutsideDefaultContext()
    {
        var loader = new RoslynCodeFixLoader(CreateNullLogger<RoslynCodeFixLoader>(), CreateNullLoggerFactory());

        var discovered = loader.DiscoverCodeFixProviderAssemblies(Path.GetDirectoryName(_assemblyPath)!);

        Assert.That(discovered, Is.Empty, "the probe assembly has no code fix providers");
        AssertNotInDefaultContext();
    }

    [Test]
    public async Task LoadAnalyzersFromAssemblyAsync_Twice_ReusesLoadedAssembly()
    {
        var loader = new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory());

        var first = await loader.LoadAnalyzersFromAssemblyAsync(_assemblyPath);
        var second = await loader.LoadAnalyzersFromAssemblyAsync(_assemblyPath);

        Assert.That(second, Has.Length.EqualTo(first.Length));
        var loaded = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .Count(a => a.GetName().Name == _assemblyName);
        Assert.That(loaded, Is.EqualTo(1));
    }

    private void AssertNotInDefaultContext()
    {
        Assert.That(AssemblyLoadContext.Default.Assemblies.Select(a => a.GetName().Name), Does.Not.Contain(_assemblyName));
        var loaded = AssemblyLoadContext.All.SelectMany(c => c.Assemblies).Single(a => a.GetName().Name == _assemblyName);
        Assert.That(AssemblyLoadContext.GetLoadContext(loaded), Is.Not.SameAs(AssemblyLoadContext.Default));
    }
}
