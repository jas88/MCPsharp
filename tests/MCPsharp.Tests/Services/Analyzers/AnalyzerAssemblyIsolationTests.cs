using System.Runtime.Loader;
using MCPsharp.Services.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
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
    private const string AnalyzerSource = """
        using System.Collections.Immutable;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.Diagnostics;

        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        public sealed class IsolationProbeAnalyzer : DiagnosticAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = new(
                "PROBE001", "Probe", "Probe", "Testing", DiagnosticSeverity.Info, isEnabledByDefault: true);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

            public override void Initialize(AnalysisContext context)
            {
                context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                context.EnableConcurrentExecution();
            }
        }
        """;

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
        EmitAnalyzerAssembly(_assemblyName, _assemblyPath);
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

    private static void EmitAnalyzerAssembly(string assemblyName, string path)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(DiagnosticAnalyzer).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(AnalyzerSource)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(path);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
