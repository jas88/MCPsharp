using System.Collections.Immutable;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.Diagnostics;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerServiceTests
{
    private static readonly string[] ExplicitAnalyzerId = ["explicit"];

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_LoadsTheAnalyzersTheOpenProjectReferences()
    {
        using var workspace = Substitute.For<RoslynWorkspace>();
        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds));
    }

    [Test]
    public async Task EnsureAnalyzersLoadedAsync_AfterTheProjectChanged_ReplacesAutoLoadedAnalyzersButKeepsExplicitOnes()
    {
        using var workspace = Substitute.For<RoslynWorkspace>();
        using var autoLoad = CreateAutoLoadService(workspace);
        var service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger, autoLoad);

        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var explicitAnalyzer = CreateMockAnalyzer(ExplicitAnalyzerId[0]);
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(explicitAnalyzer));
        await service.LoadAnalyzersAsync(assemblyPath);

        workspace.GetAnalyzerReferences().Returns(new AnalyzerReference[] { TestAnalyzerReferences.BuiltInAnalyzers() });
        await service.EnsureAnalyzersLoadedAsync();
        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id),
            Is.EquivalentTo(TestAnalyzerReferences.BuiltInAnalyzerIds.Concat(ExplicitAnalyzerId)));

        // Another project, which references no analyzers, is opened
        workspace.GetAnalyzerReferences().Returns(Array.Empty<AnalyzerReference>());
        await service.EnsureAnalyzersLoadedAsync();

        Assert.That(service.GetLoadedAnalyzers().Select(a => a.Id), Is.EqualTo(ExplicitAnalyzerId));
    }

    private static AnalyzerAutoLoadService CreateAutoLoadService(RoslynWorkspace workspace) =>
        new(CreateNullLogger<AnalyzerAutoLoadService>(),
            new RoslynAnalyzerLoader(CreateNullLogger<RoslynAnalyzerLoader>(), CreateNullLoggerFactory()),
            new AnalyzerRegistry(CreateNullLogger<AnalyzerRegistry>()),
            workspace);
}
