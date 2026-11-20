using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.Diagnostics;
using NSubstitute;
using NUnit.Framework;
using AnalyzerOptions = MCPsharp.Models.Analyzers.AnalyzerOptions;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// RunAnalyzersAsync runs on each file the analyzers of the projects that compile it, as an IDE does, plus the
/// analyzers tied to no project. Here a "src" project references MCPsharp.Analyzers and a "tests" project a probe.
/// </summary>
public partial class RoslynAnalyzerServiceTests
{
    private static readonly string StaticMethodAnalyzerId = RoslynAnalyzerAdapter.GetId(typeof(StaticMethodAnalyzer));
    private static readonly string[] TestsOnlyAnalyzerName = ["TestsOnlyAnalyzer"];

    private AnalyzerFileReference EmitTestsProjectAnalyzer()
    {
        var assemblyName = $"TestsProbe_{Guid.NewGuid():N}";
        var path = Path.Combine(CreateTestDirectory(Guid.NewGuid().ToString("N")), assemblyName + ".dll");
        AnalyzerAssemblyEmitter.Emit(assemblyName, path, AnalyzerAssemblyEmitter.AnalyzerSource("TestsOnlyAnalyzer", "PROBE_T"));
        return TestAnalyzerReferences.Create(path);
    }

    /// <summary>
    /// A workspace whose src and tests projects reference <paramref name="src"/> and <paramref name="tests"/>
    /// </summary>
    private static RoslynWorkspace CreateTwoProjectWorkspace(AnalyzerReference src, AnalyzerReference tests)
    {
        var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new[] { src, tests });
        workspace.GetFileAnalyzerScope(Arg.Any<string>()).Returns(FileAnalyzerScope.NoProject);
        return workspace;
    }

    private void HostReports(Func<string, string, ImmutableArray<Finding>> findings) =>
        _mockAnalyzerHost.RunAnalyzerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => new AnalyzerResult
            {
                Success = true,
                AnalyzerId = call.ArgAt<string>(0),
                Findings = [.. findings(call.ArgAt<string>(0), call.ArgAt<string>(1))]
            });

    private static Finding SameFinding(string file) =>
        new() { RuleId = "DUP001", Message = "The same diagnostic", FilePath = file, LineNumber = 1, ColumnNumber = 14 };

    [Test]
    public async Task RunAnalyzersAsync_OnAProjectFile_RunsOnlyTheAnalyzersOfItsProject()
    {
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var tests = EmitTestsProjectAnalyzer();
        var srcFile = CreateTestFile("public class Calc { }", ".cs", "src");
        var testFile = CreateTestFile("public class CalcTests { }", ".cs", "tests");
        using var workspace = CreateTwoProjectWorkspace(src, tests);
        workspace.GetFileAnalyzerScope(srcFile).Returns(new FileAnalyzerScope(1, [src]));
        workspace.GetFileAnalyzerScope(testFile).Returns(new FileAnalyzerScope(1, [tests]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, _) => []);

        var srcRun = await service.RunAnalyzersAsync(srcFile);
        var testRun = await service.RunAnalyzersAsync(testFile);

        Assert.That(srcRun.Success, Is.True, srcRun.ErrorMessage);
        Assert.That(srcRun.AnalyzersRun, Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        Assert.That(testRun.AnalyzersRun, Has.Length.EqualTo(1));
        Assert.That(testRun.AnalyzersRun[0], Does.StartWith("Roslyn_TestsOnlyAnalyzer@"));
    }

    [Test]
    public async Task RunAnalyzersAsync_OnAFileInNoProject_RunsOnlyTheAnalyzersTiedToNoProject()
    {
        var file = CreateTestFile("public class Script { }", ".cs");
        using var workspace = CreateTwoProjectWorkspace(TestAnalyzerReferences.BuiltInAnalyzers(), EmitTestsProjectAnalyzer());
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var explicitAnalyzer = CreateMockAnalyzer("explicit");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(explicitAnalyzer));
        await service.LoadAnalyzersAsync(assemblyPath);
        HostReports((_, _) => []);

        var result = await service.RunAnalyzersAsync(file);

        Assert.That(result.AnalyzersRun, Is.EqualTo(ExplicitAnalyzerId));
    }

    [Test]
    public async Task RunAnalyzersAsync_OnADirectory_RunsEachFilesOwnProjectsAnalyzers()
    {
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var tests = EmitTestsProjectAnalyzer();
        var directory = CreateTestDirectory("solution");
        var srcFile = CreateTestFile("public class Calc { }", ".cs", Path.Combine("solution", "src"));
        var testFile = CreateTestFile("public class CalcTests { }", ".cs", Path.Combine("solution", "tests"));
        var generatedFile = CreateTestFile("public class Generated { }", ".cs", Path.Combine("solution", "src", "obj"));
        using var workspace = CreateTwoProjectWorkspace(src, tests);
        workspace.GetFileAnalyzerScope(srcFile).Returns(new FileAnalyzerScope(1, [src]));
        workspace.GetFileAnalyzerScope(testFile).Returns(new FileAnalyzerScope(1, [tests]));
        workspace.GetFileAnalyzerScope(generatedFile).Returns(new FileAnalyzerScope(1, [src]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, _) => []);

        var result = await service.RunAnalyzersAsync(directory);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        Assert.That(result.FilesAnalyzed, Is.EqualTo(2), "build output (obj) is not analyzed");
        Assert.That(result.Results.Where(r => r.FilePath == srcFile).Select(r => r.AnalyzerId),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
        var testFileAnalyzers = result.Results.Where(r => r.FilePath == testFile).Select(r => r.AnalyzerId).ToList();
        Assert.That(testFileAnalyzers, Has.Count.EqualTo(1));
        Assert.That(testFileAnalyzers[0], Does.StartWith("Roslyn_TestsOnlyAnalyzer@"));
        await _mockAnalyzerHost.DidNotReceive().RunAnalyzerAsync(
            Arg.Any<string>(), generatedFile, Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzersAsync_OnAFileInTwoProjects_DeduplicatesIdenticalDiagnostics()
    {
        // A linked file, or a multi-targeted project, compiled with two builds of the same analyzers
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var tests = EmitTestsProjectAnalyzer();
        var sharedFile = CreateTestFile("public class Shared { }", ".cs");
        using var workspace = CreateTwoProjectWorkspace(src, tests);
        workspace.GetFileAnalyzerScope(sharedFile).Returns(new FileAnalyzerScope(2, [src, tests]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, file) => [SameFinding(file)]);

        var result = await service.RunAnalyzersAsync(sharedFile);

        Assert.That(result.AnalyzersRun, Has.Length.EqualTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Length + 1));
        Assert.That(result.Results.SelectMany(r => r.Issues), Has.Exactly(1).Items);
        Assert.That(result.TotalIssuesFound, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAnalyzersAsync_OnAFileInOneProject_KeepsIdenticalDiagnosticsOfDifferentAnalyzers()
    {
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var srcFile = CreateTestFile("public class Calc { }", ".cs");
        using var workspace = CreateTwoProjectWorkspace(src, EmitTestsProjectAnalyzer());
        workspace.GetFileAnalyzerScope(srcFile).Returns(new FileAnalyzerScope(1, [src]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, file) => [SameFinding(file)]);

        var result = await service.RunAnalyzersAsync(srcFile);

        Assert.That(result.TotalIssuesFound, Is.EqualTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Length));
    }

    [Test]
    public async Task RunAnalyzersAsync_WithAnalyzerIds_RunsTheRequestedAnalyzersThatApplyToTheFile()
    {
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var tests = EmitTestsProjectAnalyzer();
        var srcFile = CreateTestFile("public class Calc { }", ".cs");
        using var workspace = CreateTwoProjectWorkspace(src, tests);
        workspace.GetFileAnalyzerScope(srcFile).Returns(new FileAnalyzerScope(1, [src]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, _) => []);
        await service.EnsureAnalyzersLoadedAsync();
        var testsAnalyzerId = service.GetLoadedAnalyzers().Single(a => a.Name == "TestsOnlyAnalyzer").Id;

        var result = await service.RunAnalyzersAsync(srcFile, [StaticMethodAnalyzerId, testsAnalyzerId]);
        var onlyOtherProjects = await service.RunAnalyzersAsync(srcFile, [testsAnalyzerId]);

        Assert.That(result.AnalyzersRun, Is.EqualTo(new[] { StaticMethodAnalyzerId }));
        Assert.That(onlyOtherProjects.Success, Is.False);
        Assert.That(onlyOtherProjects.ErrorMessage, Does.Contain("apply"));
    }

    [Test]
    public async Task RunAnalyzersAsync_OnADirectoryOverTheFileLimit_AnalyzesTheFirstFilesAndCountsTheRest()
    {
        for (var i = 0; i <= RoslynAnalyzerService.MaxDirectoryFiles; i++)
        {
            CreateTestFile($"public class C{i} {{ }}", ".cs", "many");
        }

        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("explicit");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));
        await _service.LoadAnalyzersAsync(assemblyPath);
        HostReports((_, _) => []);

        var result = await _service.RunAnalyzersAsync(Path.Combine(TempDirectory, "many"));

        Assert.That(result.FilesAnalyzed, Is.EqualTo(RoslynAnalyzerService.MaxDirectoryFiles));
        Assert.That(result.FilesSkipped, Is.EqualTo(1));
    }

    /// <summary>
    /// A copy of MCPsharp.Analyzers.dll in a directory of its own: the same assembly at another path, as two package
    /// restores or two output folders give
    /// </summary>
    private string CopyBuiltInAnalyzers(string directoryName)
    {
        var source = typeof(StaticMethodAnalyzer).Assembly.Location;
        var copy = Path.Combine(CreateTestDirectory(directoryName), Path.GetFileName(source));
        File.Copy(source, copy);
        return copy;
    }

    [Test]
    public async Task RunAnalyzersAsync_WithOneAnalyzerAssemblyReferencedFromTwoPaths_RunsItOnTheFilesOfBothProjects()
    {
        // The projects' copies of the assembly give analyzers with the same IDs; the one kept per ID must still run
        // on the files of both projects
        var first = TestAnalyzerReferences.Create(CopyBuiltInAnalyzers("first-analyzers"));
        var second = TestAnalyzerReferences.Create(CopyBuiltInAnalyzers("second-analyzers"));
        var firstFile = CreateTestFile("public class First { }", ".cs", "first");
        var secondFile = CreateTestFile("public class Second { }", ".cs", "second");
        using var workspace = CreateTwoProjectWorkspace(first, second);
        workspace.GetFileAnalyzerScope(firstFile).Returns(new FileAnalyzerScope(1, [first]));
        workspace.GetFileAnalyzerScope(secondFile).Returns(new FileAnalyzerScope(1, [second]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, _) => []);

        var firstRun = await service.RunAnalyzersAsync(firstFile);
        var secondRun = await service.RunAnalyzersAsync(secondFile);

        Assert.That(firstRun.AnalyzersRun, Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds), firstRun.ErrorMessage);
        Assert.That(secondRun.AnalyzersRun, Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds), secondRun.ErrorMessage);
    }

    [Test]
    public async Task RunAnalyzersAsync_WithACustomPathCopyOfAProjectsAnalyzers_RunsThemOnFilesInNoProject()
    {
        // Analyzers from customAnalyzerPaths belong to no project and run on every file, even when a project
        // references the same analyzers, which have the same IDs
        var custom = CopyBuiltInAnalyzers("custom-analyzers");
        var script = CreateTestFile("public class Script { }", ".cs");
        using var workspace = CreateTwoProjectWorkspace(TestAnalyzerReferences.BuiltInAnalyzers(), EmitTestsProjectAnalyzer());
        using var autoLoad = new AnalyzerAutoLoadService(
            CreateNullLogger<AnalyzerAutoLoadService>(),
            new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory()),
            new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>()),
            workspace,
            new AnalyzerAutoLoadConfiguration { CustomAnalyzerPaths = [custom] });
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        HostReports((_, _) => []);

        var result = await service.RunAnalyzersAsync(script);

        Assert.That(result.AnalyzersRun, Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds), result.ErrorMessage);
    }

    [Test]
    public async Task GetAnalyzersForFile_ReturnsTheAnalyzersOfTheFilesProjects()
    {
        var src = TestAnalyzerReferences.BuiltInAnalyzers();
        var testFile = CreateTestFile("public class CalcTests { }", ".cs");
        var tests = EmitTestsProjectAnalyzer();
        using var workspace = CreateTwoProjectWorkspace(src, tests);
        workspace.GetFileAnalyzerScope(testFile).Returns(new FileAnalyzerScope(1, [tests]));
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);
        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(service.GetAnalyzersForFile(testFile).Select(a => a.Name), Is.EqualTo(TestsOnlyAnalyzerName));
    }
}
