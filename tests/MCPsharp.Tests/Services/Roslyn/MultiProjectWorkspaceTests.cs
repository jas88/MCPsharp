using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using SymbolKind = MCPsharp.Services.Roslyn.SymbolKind;

namespace MCPsharp.Tests.Services.Roslyn;

/// <summary>
/// Read-only queries against a loaded multi-project solution (library + two consumers,
/// with the library multi-targeted). The solution is generated and restored once per fixture.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MultiProjectWorkspaceTests
{
    private static readonly string[] ProjectFiles = new[] { "Lib.csproj", "App.csproj", "Extras.csproj" };
    private static readonly string[] SourceFiles = new[] { "Greeter.cs", "Shape.cs", "Program.cs", "Square.cs" };
    private static readonly string[] FormatCallerMethods = new[] { "Greet", "Describe" };
    private static readonly string[] ShapeSubclasses = new[] { "Circle", "Square" };
    private static readonly string[] FormatFiles = new[] { "Greeter.cs", "Square.cs" };
    private static readonly string[] ProjectNames = new[] { "Lib", "App", "Extras" };

    private MultiProjectSolutionFixture _solution = null!;
    private RoslynWorkspace _workspace = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _solution = await MultiProjectSolutionFixture.CreateAsync();
        _workspace = new RoslynWorkspace();
        await _workspace.InitializeAsync(_solution.SolutionPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _workspace?.Dispose();
        _solution?.Dispose();
    }

    [Test]
    public void Initialize_LoadsEveryProjectOfTheSolution()
    {
        var health = _workspace.GetHealth();

        // Lib is multi-targeted, so Roslyn sees four projects; they collapse to three primary projects
        Assert.That(_workspace.Solution.ProjectIds, Has.Count.EqualTo(4));
        Assert.That(_workspace.GetProjects().Select(p => Path.GetFileName(p.FilePath)),
            Is.EquivalentTo(ProjectFiles));
        Assert.That(_workspace.GetCompilations(), Has.Count.EqualTo(3));
        Assert.That(health.TotalProjects, Is.EqualTo(3));
        Assert.That(health.LoadedProjects, Is.EqualTo(3));
        Assert.That(health.ErrorCount, Is.EqualTo(0), "Generated solution should compile cleanly");
    }

    [Test]
    public void GetAllDocuments_ReturnsDocumentsFromAllProjectsOnce()
    {
        var fileNames = _workspace.GetAllDocuments()
            .Select(d => d.FilePath)
            .OfType<string>()
            .ToList();

        Assert.That(fileNames, Is.Unique);
        foreach (var expected in SourceFiles)
        {
            Assert.That(fileNames.Count(f => Path.GetFileName(f) == expected), Is.EqualTo(1),
                $"{expected} should be visible exactly once");
        }
    }

    [Test]
    public void GetSourceTrees_ReturnsEachFileOnce()
    {
        var paths = _workspace.GetSourceTrees().Select(t => t.FilePath).ToList();

        Assert.That(paths, Is.Unique);
        Assert.That(paths.Count(p => Path.GetFileName(p) == "Greeter.cs"), Is.EqualTo(1));
        Assert.That(paths.Count(p => Path.GetFileName(p) == "Square.cs"), Is.EqualTo(1));
    }

    [Test]
    public void GetDocument_ResolvesToTheOwningProject()
    {
        var appDocument = _workspace.GetDocument(_solution.AppProgramPath);
        var extrasDocument = _workspace.GetDocument(_solution.ExtrasSquarePath);

        Assert.That(appDocument?.Project.Name, Is.EqualTo("App"));
        Assert.That(extrasDocument?.Project.Name, Is.EqualTo("Extras"));
        Assert.That(_workspace.GetDocuments(_solution.LibGreeterPath), Has.Count.EqualTo(2),
            "A multi-targeted file has one document per target framework");
    }

    [Test]
    public void GetSemanticModel_UsesTheCompilationOfTheOwningProject()
    {
        var tree = _workspace.GetSourceTrees().Single(t => Path.GetFileName(t.FilePath) == "Square.cs");

        var model = _workspace.GetSemanticModel(tree);

        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Compilation.AssemblyName, Is.EqualTo("Extras"));
        Assert.That(model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    [Test]
    public void FindSourceSymbols_FindsSymbolsDeclaredInAnyProject()
    {
        Assert.That(_workspace.FindSourceSymbols("Circle", Microsoft.CodeAnalysis.SymbolFilter.Type), Has.Count.EqualTo(1));
        Assert.That(_workspace.FindSourceSymbols("Square", Microsoft.CodeAnalysis.SymbolFilter.Type), Has.Count.EqualTo(1));
        Assert.That(_workspace.FindSourceSymbols("Shape", Microsoft.CodeAnalysis.SymbolFilter.Type), Has.Count.EqualTo(1));
        Assert.That(_workspace.GetTypeByMetadataName("Extras.Square"), Is.Not.Null);
    }

    [Test]
    public async Task FindReferences_FindsUsagesInOtherProjects()
    {
        var service = new ReferenceFinderService(_workspace);

        var result = await service.FindReferencesAsync(symbolName: "Format");

        Assert.That(result, Is.Not.Null);
        var files = result!.References.Select(r => Path.GetFileName(r.File)).ToList();
        Assert.That(files, Does.Contain("Greeter.cs"), "call inside Lib");
        Assert.That(files, Does.Contain("Square.cs"), "call from Extras");
        Assert.That(result.References.Select(r => (r.File, r.Line, r.Column)), Is.Unique,
            "multi-targeted Lib must not produce duplicate reference locations");
    }

    [Test]
    public async Task FindCallers_FindsCallersAcrossProjects()
    {
        var service = new CallerAnalysisService(_workspace, new SymbolQueryService(_workspace));

        var formatCallers = await service.FindCallersAsync("Format", "Greeter");
        var greetCallers = await service.FindCallersAsync("Greet", "Greeter");

        Assert.That(formatCallers, Is.Not.Null);
        Assert.That(formatCallers!.Callers.Select(c => c.CallerMethod), Is.SupersetOf(FormatCallerMethods));
        Assert.That(formatCallers.Callers.Select(c => (c.File, c.Line, c.Column)), Is.Unique);

        Assert.That(greetCallers, Is.Not.Null);
        Assert.That(greetCallers!.Callers.Any(c => c.CallerMethod == "Main" && Path.GetFileName(c.File) == "Program.cs"),
            Is.True, "Program.Main in App calls Greeter.Greet defined in Lib");
    }

    [Test]
    public async Task TypeUsages_FindsUsagesAndDerivedTypesAcrossProjects()
    {
        var service = new TypeUsageService(_workspace, new SymbolQueryService(_workspace));

        var usages = await service.FindTypeUsagesAsync("Shape");
        var inheritance = await service.AnalyzeInheritanceAsync("Shape");

        Assert.That(usages, Is.Not.Null);
        var usageFiles = usages!.Usages.Select(u => Path.GetFileName(u.File)).ToList();
        Assert.That(usageFiles, Does.Contain("Program.cs"));
        Assert.That(usageFiles, Does.Contain("Square.cs"));
        Assert.That(usageFiles, Does.Contain("Shape.cs"));

        Assert.That(inheritance.DerivedClasses.Select(d => d.MemberName),
            Is.EquivalentTo(ShapeSubclasses));
    }

    [Test]
    public async Task RenamePreview_ChangesEveryReferencingProjectOnce()
    {
        var service = CreateRenameService(_workspace);

        var result = await service.RenameSymbolAsync(new RenameRequest
        {
            OldName = "Format",
            NewName = "FormatName",
            SymbolKind = SymbolKind.Method,
            ContainingType = "Greeter",
            PreviewOnly = true,
            ForcePublicApiChange = true
        });

        Assert.That(result.Success, Is.True, string.Join("; ", result.Errors));
        Assert.That(result.FilesModified.Select(Path.GetFileName), Is.EquivalentTo(FormatFiles));
        Assert.That(result.Preview, Is.Not.Null);
        Assert.That(result.Preview!.Select(p => p.FilePath), Is.Unique);
        Assert.That(result.RenamedCount, Is.EqualTo(3), "declaration + call in Lib + call in Extras");
    }

    [Test]
    public async Task InitializeAsync_WithSlnx_LoadsEveryProject()
    {
        using var workspace = new RoslynWorkspace();

        await workspace.InitializeAsync(_solution.SlnxPath);

        Assert.That(workspace.GetProjects().Select(p => p.Name.Split('(')[0]),
            Is.EquivalentTo(ProjectNames));
        Assert.That(workspace.GetHealth().ErrorCount, Is.EqualTo(0));
    }

    internal static RenameSymbolService CreateRenameService(RoslynWorkspace workspace)
    {
        var symbolQuery = new SymbolQueryService(workspace);
        var referenceFinder = new AdvancedReferenceFinderService(
            workspace,
            symbolQuery,
            Substitute.For<ICallerAnalysisService>(),
            Substitute.For<ICallChainService>(),
            Substitute.For<ITypeUsageService>());

        return new RenameSymbolService(workspace, referenceFinder, symbolQuery, NullLogger<RenameSymbolService>.Instance);
    }
}

/// <summary>
/// Tests that modify a multi-project solution; each test gets a freshly generated solution.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MultiProjectWorkspaceMutationTests
{
    private static readonly string[] ShapeFiles = new[] { "Shape.cs", "Program.cs", "Square.cs" };

    private MultiProjectSolutionFixture _solution = null!;
    private RoslynWorkspace _workspace = null!;

    [SetUp]
    public async Task SetUp()
    {
        _solution = await MultiProjectSolutionFixture.CreateAsync();
        _workspace = new RoslynWorkspace();
        await _workspace.InitializeAsync(_solution.SolutionPath);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
        _solution?.Dispose();
    }

    [Test]
    public async Task UpdateDocument_InLibrary_IsVisibleFromDependentProjects()
    {
        var original = await File.ReadAllTextAsync(_solution.LibGreeterPath);
        var updated = original.Replace(
            "public static string Format(string name)",
            "public string Shout(string name)\n        {\n            return name;\n        }\n\n        public static string Format(string name)",
            System.StringComparison.Ordinal);

        var applied = await _workspace.UpdateDocumentAsync(_solution.LibGreeterPath, updated);

        Assert.That(applied, Is.True);
        var documents = _workspace.GetDocuments(_solution.LibGreeterPath);
        Assert.That(documents, Has.Count.EqualTo(2));
        foreach (var document in documents)
        {
            var text = await document.GetTextAsync();
            Assert.That(text.ToString(), Does.Contain("Shout"),
                $"{document.Project.Name}: every target framework copy of the file is updated");
        }

        var appCompilation = _workspace.GetCompilationForFile(_solution.AppProgramPath);
        var greeterAsSeenByApp = appCompilation?.GetTypeByMetadataName("Lib.Greeter");
        Assert.That(greeterAsSeenByApp?.GetMembers("Shout"), Is.Not.Empty,
            "the dependent App compilation is refreshed after a change in Lib");
    }

    [Test]
    public async Task Rename_AppliedAcrossProjects_UpdatesFilesAndCompilations()
    {
        var service = MultiProjectWorkspaceTests.CreateRenameService(_workspace);

        var result = await service.RenameSymbolAsync(new RenameRequest
        {
            OldName = "Shape",
            NewName = "Figure",
            SymbolKind = SymbolKind.Class,
            ForcePublicApiChange = true
        });

        Assert.That(result.Success, Is.True, string.Join("; ", result.Errors));
        Assert.That(result.FilesModified.Select(Path.GetFileName),
            Is.EquivalentTo(ShapeFiles));
        Assert.That(await File.ReadAllTextAsync(_solution.ExtrasSquarePath), Does.Contain("class Square : Figure"));
        Assert.That(await File.ReadAllTextAsync(_solution.AppProgramPath), Does.Contain("Figure shape = new Circle(2);"));

        Assert.That(_workspace.FindSourceSymbols("Figure", Microsoft.CodeAnalysis.SymbolFilter.Type), Has.Count.EqualTo(1));
        Assert.That(_workspace.FindSourceSymbols("Shape", Microsoft.CodeAnalysis.SymbolFilter.Type), Is.Empty);
        foreach (var compilation in _workspace.GetCompilations())
        {
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
                $"{compilation.AssemblyName} should still compile after the rename");
        }
    }
}
