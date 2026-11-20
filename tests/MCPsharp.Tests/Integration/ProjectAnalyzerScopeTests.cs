using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.Fixes;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.Services.Analyzers;
using MCPsharp.Tests.Services.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Running analyzers on a real, restored solution whose projects reference different analyzers, as an IDE would:
/// Src and Tests each reference a probe of their own and their own build of a "style" probe (as MCPsharp's own
/// projects take the code style analyzers from the NuGet package and from the SDK), and Shared.cs is linked into both.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public class ProjectAnalyzerScopeTests
{
    private const string StyleRule = "PROBE_STYLE";
    private const string SrcRule = "PROBE_SRC";
    private const string TestsRule = "PROBE_TESTS";

    private static readonly string[] SrcRules = [SrcRule, StyleRule];
    private static readonly string[] TestsRules = [StyleRule, TestsRule];
    private static readonly string[] SharedRules = [SrcRule, StyleRule, TestsRule];
    private static readonly string[] SrcRuleOnly = [SrcRule];

    private string _root = null!;
    private RoslynWorkspace _workspace = null!;
    private AnalyzerAutoLoadService _autoLoad = null!;
    private RoslynAnalyzerService _service = null!;

    private string SrcFile => Path.Combine(_root, "Src", "Calc.cs");

    private string TestsFile => Path.Combine(_root, "Tests", "CalcTests.cs");

    private string SharedFile => Path.Combine(_root, "Shared", "Shared.cs");

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "MCPsharp_AnalyzerScope", Guid.NewGuid().ToString("N"));

        // Isolate the solution from any Directory.Build.* or central package management above the temp directory
        Write("Directory.Build.props", "<Project />");
        Write("Directory.Build.targets", "<Project />");
        Write("Directory.Packages.props", "<Project />");

        EmitProbe("ProbeStyle", "1.0.0.0", "style-v1", "ProbeStyleAnalyzer", StyleRule);
        EmitProbe("ProbeStyle", "2.0.0.0", "style-v2", "ProbeStyleAnalyzer", StyleRule);
        EmitProbe("ProbeSrc", "1.0.0.0", "src", "SrcOnlyAnalyzer", SrcRule);
        EmitProbe("ProbeTests", "1.0.0.0", "tests", "TestsOnlyAnalyzer", TestsRule);

        Write(Path.Combine("Src", "Src.csproj"), Project("style-v1/ProbeStyle.dll", "src/ProbeSrc.dll"));
        Write(Path.Combine("Tests", "Tests.csproj"), Project("style-v2/ProbeStyle.dll", "tests/ProbeTests.dll"));
        Write(Path.Combine("Src", "Calc.cs"), "namespace Src { public class Calc { } }");
        Write(Path.Combine("Tests", "CalcTests.cs"), "namespace Tests { public class CalcTests { } }");
        Write(Path.Combine("Shared", "Shared.cs"), "namespace Shared { public class Helper { } }");
        Write("Scope.slnx", """
            <Solution>
              <Project Path="Src/Src.csproj" />
              <Project Path="Tests/Tests.csproj" />
            </Solution>
            """);

        var solutionPath = Path.Combine(_root, "Scope.slnx");
        await MultiProjectSolutionFixture.RestoreAsync(solutionPath, _root);
        _workspace = new RoslynWorkspace();
        await _workspace.InitializeAsync(solutionPath);

        var loggerFactory = NullLoggerFactory.Instance;
        var registry = new AnalyzerRegistry(NullLogger<AnalyzerRegistry>.Instance);
        var loader = new RoslynAnalyzerLoader(NullLogger<RoslynAnalyzerLoader>.Instance, loggerFactory);
        _autoLoad = new AnalyzerAutoLoadService(NullLogger<AnalyzerAutoLoadService>.Instance, loader, registry, _workspace);
        var securityManager = new SecurityManager(NullLogger<SecurityManager>.Instance, _root);
        var host = new AnalyzerHost(
            NullLogger<AnalyzerHost>.Instance,
            loggerFactory,
            registry,
            securityManager,
            new FixEngine(NullLogger<FixEngine>.Instance, new FileOperationsService(_root)),
            new DefaultAnalyzerSandboxFactory(loggerFactory, securityManager));
        _service = new RoslynAnalyzerService(loader, host, NullLogger<RoslynAnalyzerService>.Instance, _autoLoad, registry);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _autoLoad?.Dispose();
        _workspace?.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; loaded analyzer assemblies can stay locked on Windows
        }
    }

    [Test]
    public async Task SrcFile_GetsOnlyTheDiagnosticsOfTheSrcProjectsAnalyzers()
    {
        Assert.That(await ProbeRulesReportedAsync(SrcFile), Is.EqualTo(SrcRules));
    }

    [Test]
    public async Task TestsFile_GetsOnlyTheDiagnosticsOfTheTestsProjectsAnalyzers()
    {
        Assert.That(await ProbeRulesReportedAsync(TestsFile), Is.EqualTo(TestsRules));
    }

    [Test]
    public async Task FileLinkedIntoBothProjects_GetsBothProjectsDiagnosticsOnce()
    {
        // Both builds of the style analyzer report the same diagnostic on the shared file, which is reported once
        Assert.That(await ProbeRulesReportedAsync(SharedFile), Is.EqualTo(SharedRules));
    }

    [Test]
    public async Task Directory_RunsEachFilesOwnProjectsAnalyzers()
    {
        var result = await _service.RunAnalyzersAsync(_root);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var rulesByFile = result.Results
            .SelectMany(r => r.Issues)
            .Where(i => i.RuleId.StartsWith("PROBE_", StringComparison.Ordinal))
            .GroupBy(i => Path.GetFileName(i.FilePath))
            .ToDictionary(g => g.Key, g => g.Select(i => i.RuleId).Order(StringComparer.Ordinal).ToArray());
        Assert.That(rulesByFile["Calc.cs"], Is.EqualTo(SrcRules));
        Assert.That(rulesByFile["CalcTests.cs"], Is.EqualTo(TestsRules));
        Assert.That(rulesByFile["Shared.cs"], Is.EqualTo(SharedRules));
    }

    [Test]
    public async Task AnalyzerIds_RunOnlyTheRequestedAnalyzers()
    {
        await _service.EnsureAnalyzersLoadedAsync();
        var srcOnly = _service.GetLoadedAnalyzers().Single(a => a.Name == "SrcOnlyAnalyzer").Id;

        Assert.That(await ProbeRulesReportedAsync(SrcFile, [srcOnly]), Is.EqualTo(SrcRuleOnly));
    }

    [Test]
    public async Task GetAnalyzersForFile_ReturnsOnlyTheFilesProjectsAnalyzers()
    {
        // What apply_roslyn_fixes computes fixes with
        await _service.EnsureAnalyzersLoadedAsync();

        var names = _service.GetAnalyzersForFile(SrcFile).Select(a => a.Name).ToList();

        Assert.That(names, Does.Contain("SrcOnlyAnalyzer"));
        Assert.That(names, Does.Not.Contain("TestsOnlyAnalyzer"));
        Assert.That(names.Count(n => n == "ProbeStyleAnalyzer"), Is.EqualTo(1), "only the Src project's build");
    }

    private async Task<string[]> ProbeRulesReportedAsync(string targetPath, IEnumerable<string>? analyzerIds = null)
    {
        var result = await _service.RunAnalyzersAsync(targetPath, analyzerIds);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        return result.Results
            .SelectMany(r => r.Issues)
            .Select(i => i.RuleId)
            .Where(id => id.StartsWith("PROBE_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Project(params string[] analyzers) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
            <EnableNETAnalyzers>false</EnableNETAnalyzers>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="../Shared/Shared.cs" Link="Shared.cs" />
            {string.Join(" ", analyzers.Select(a => $"<Analyzer Include=\"../analyzers/{a}\" />"))}
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Compile a probe analyzer reporting <paramref name="ruleId"/> on every class into analyzers/<paramref name="directory"/>
    /// </summary>
    private void EmitProbe(string assemblyName, string version, string directory, string className, string ruleId)
    {
        var path = Path.Combine(_root, "analyzers", directory, assemblyName + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AnalyzerAssemblyEmitter.Emit(assemblyName, path, AnalyzerAssemblyEmitter.ReportingAnalyzerSource(className, ruleId), version);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
