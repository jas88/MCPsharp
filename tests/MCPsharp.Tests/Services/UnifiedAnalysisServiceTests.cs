using MCPsharp.Models;
using MCPsharp.Models.Architecture;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services;
using MCPsharp.Services.Consolidated;
using NSubstitute;
using NUnit.Framework;
using ArchitectureDefinition = MCPsharp.Models.Architecture.ArchitectureDefinition;
using ArchitectureWarning = MCPsharp.Models.Architecture.ArchitectureWarning;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Project, architecture and quality analysis must operate on the project path (or focus file),
/// never on the scope name or on the parent of the project directory.
/// </summary>
[TestFixture]
public class UnifiedAnalysisServiceTests
{
    private const string CalculatorSource = @"public class Calculator
{
    public int Add(int a, int b)
    {
        if (a > b)
        {
            return a + b;
        }
        return b + a;
    }
}
";

    private const string ProjectFile = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Example.Package"" Version=""1.0.0"" />
  </ItemGroup>
</Project>
";

    private static readonly string[] ExpectedPackages = { "Example.Package" };

    private string _parent = null!;
    private string _projectPath = null!;
    private string _calculatorPath = null!;

    [SetUp]
    public void SetUp()
    {
        _parent = Path.Combine(Path.GetTempPath(), $"mcpsharp_unified_{Guid.NewGuid():N}");
        _projectPath = Path.Combine(_parent, "App");
        Directory.CreateDirectory(Path.Combine(_projectPath, "Tests"));
        _calculatorPath = Path.Combine(_projectPath, "Calculator.cs");
        File.WriteAllText(_calculatorPath, CalculatorSource);
        File.WriteAllText(Path.Combine(_projectPath, "Tests", "CalculatorTests.cs"), "public class CalculatorTests { }\n");
        File.WriteAllText(Path.Combine(_projectPath, "App.csproj"), ProjectFile);

        // A sibling file next to the project directory must never be analysed as part of the project
        File.WriteAllText(Path.Combine(_parent, "Outside.cs"), "public class Outside { }\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, true);
        }
    }

    #region analyze_project

    [Test]
    public async Task AnalyzeProject_ProjectDirectory_InventoriesOnlyFilesInsideIt()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeProjectAsync(new ProjectAnalysisRequest
        {
            ProjectPath = _projectPath,
            AnalysisTypes = new List<ProjectAnalysisType> { ProjectAnalysisType.All },
            Options = new ToolOptions { Detail = DetailLevel.Standard }
        });

        Assert.That(response.Error, Is.Null);
        var files = response.Files!.Select(f => f.Path).ToList();
        Assert.That(files, Has.Count.EqualTo(2));
        Assert.That(files, Has.All.StartsWith(_projectPath + Path.DirectorySeparatorChar));
        Assert.That(files, Has.None.EndsWith("Outside.cs"));
    }

    [Test]
    public async Task AnalyzeProject_ProjectDirectory_ReadsProjectFileInsideIt()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeProjectAsync(new ProjectAnalysisRequest
        {
            ProjectPath = _projectPath,
            AnalysisTypes = new List<ProjectAnalysisType> { ProjectAnalysisType.All }
        });

        Assert.That(response.Dependencies!.PackageReferences.Select(p => p.Name), Is.EqualTo(ExpectedPackages));
        Assert.That(response.BuildInfo!.BuildConfigurations, Does.Contain("TargetFramework: net10.0"));
    }

    [Test]
    public async Task AnalyzeProject_ProjectFile_InventoriesItsDirectory()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeProjectAsync(new ProjectAnalysisRequest
        {
            ProjectPath = Path.Combine(_projectPath, "App.csproj"),
            AnalysisTypes = new List<ProjectAnalysisType> { ProjectAnalysisType.Dependencies },
            Options = new ToolOptions { Detail = DetailLevel.Standard }
        });

        Assert.That(response.Files!.Select(f => f.Path).ToList(), Has.Count.EqualTo(2).And.None.EndsWith("Outside.cs"));
        Assert.That(response.Dependencies!.PackageReferences, Has.Count.EqualTo(1));
    }

    #endregion

    #region analyze_architecture

    private static IArchitectureValidatorService CreateValidator()
    {
        var validator = Substitute.For<IArchitectureValidatorService>();
        validator.ValidateArchitectureAsync(Arg.Any<string>(), Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(new ArchitectureValidationResult
            {
                IsValid = true,
                Violations = new List<LayerViolation>(),
                Warnings = new List<ArchitectureWarning>(),
                TotalTypesAnalyzed = 1,
                CompliantTypes = 1,
                CompliancePercentage = 100,
                AnalysisDuration = TimeSpan.Zero,
                LayerStatistics = new Dictionary<string, int>(),
                AnalyzedFiles = new List<string>()
            });
        validator.DetectLayerViolationsAsync(Arg.Any<string>(), Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(new List<LayerViolation>());
        validator.AnalyzeDependenciesAsync(Arg.Any<string>(), Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>())
            .Returns(new DependencyAnalysisResult
            {
                Nodes = new List<MCPsharp.Models.Architecture.DependencyNode>(),
                Edges = new List<MCPsharp.Models.Architecture.DependencyEdge>(),
                CircularDependencies = new List<CircularDependency>(),
                LayerDependencies = new Dictionary<string, List<string>>(),
                DependencyMetrics = new Dictionary<string, int>(),
                AnalysisDuration = TimeSpan.Zero
            });
        return validator;
    }

    [Test]
    public async Task AnalyzeArchitecture_AllScope_PassesProjectPathToValidator()
    {
        var validator = CreateValidator();
        var service = new UnifiedAnalysisService(architectureValidator: validator);

        var response = await service.AnalyzeArchitectureAsync(new ArchitectureAnalysisRequest
        {
            Scope = AnalysisScope.All,
            ProjectPath = _projectPath
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Scope, Is.EqualTo("All"));
        Assert.That(response.Validation, Is.Not.Null);
        Assert.That(response.Violations, Is.Not.Null);
        Assert.That(response.DependencyAnalysis, Is.Not.Null);
        await validator.Received(1).ValidateArchitectureAsync(_projectPath, Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
        await validator.Received(1).AnalyzeDependenciesAsync(_projectPath, Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
        await validator.DidNotReceive().ValidateArchitectureAsync("All", Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
        // Violations come from the validation already performed rather than a second validation pass
        await validator.DidNotReceive().DetectLayerViolationsAsync(Arg.Any<string>(), Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeArchitecture_DependenciesScope_OnlyAnalysesDependencies()
    {
        var validator = CreateValidator();
        var service = new UnifiedAnalysisService(architectureValidator: validator);

        var response = await service.AnalyzeArchitectureAsync(new ArchitectureAnalysisRequest
        {
            Scope = AnalysisScope.Dependencies,
            ProjectPath = _projectPath
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Validation, Is.Null);
        Assert.That(response.Violations, Is.Null);
        Assert.That(response.DependencyAnalysis, Is.Not.Null);
        await validator.DidNotReceive().ValidateArchitectureAsync(Arg.Any<string>(), Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
        await validator.Received(1).AnalyzeDependenciesAsync(_projectPath, Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeArchitecture_ReferencesScope_ReportsViolationsOnly()
    {
        var validator = CreateValidator();
        var service = new UnifiedAnalysisService(architectureValidator: validator);

        var response = await service.AnalyzeArchitectureAsync(new ArchitectureAnalysisRequest
        {
            Scope = AnalysisScope.References,
            ProjectPath = _projectPath
        });

        Assert.That(response.Validation, Is.Null);
        Assert.That(response.Violations, Is.Not.Null);
        Assert.That(response.DependencyAnalysis, Is.Null);
        await validator.Received(1).DetectLayerViolationsAsync(_projectPath, Arg.Any<ArchitectureDefinition?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeArchitecture_WithoutProjectPath_ReturnsError()
    {
        var service = new UnifiedAnalysisService(architectureValidator: CreateValidator());

        var response = await service.AnalyzeArchitectureAsync(new ArchitectureAnalysisRequest { Scope = AnalysisScope.All });

        Assert.That(response.Error, Is.Not.Null.And.Contains("project path"));
    }

    #endregion

    #region analyze_quality

    private static readonly List<AnalysisQualityMetric> FileMetrics = new()
    {
        AnalysisQualityMetric.Complexity,
        AnalysisQualityMetric.Maintainability,
        AnalysisQualityMetric.TestCoverage,
        AnalysisQualityMetric.CodeSmells
    };

    [Test]
    public async Task AnalyzeQuality_Project_AnalysesFilesUnderProjectPath()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeQualityAsync(new QualityAnalysisRequest
        {
            Scope = AnalysisScope.All,
            Metrics = FileMetrics,
            ProjectPath = _projectPath
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Scope, Is.EqualTo("All"));
        Assert.That(response.Complexity, Is.Not.Null);
        Assert.That(response.Complexity!.FileCount, Is.EqualTo(2));
        Assert.That(response.Complexity.Scope, Is.EqualTo(_projectPath));
        Assert.That(response.Maintainability, Is.Not.Null);
        Assert.That(response.TestCoverage, Is.Not.Null);
        Assert.That(response.TestCoverage!.TotalFiles, Is.EqualTo(2));
        Assert.That(response.TestCoverage.TestFiles, Is.EqualTo(1));
        Assert.That(response.CodeSmells, Is.Not.Null);
    }

    [Test]
    public async Task AnalyzeQuality_FilePath_AnalysesOnlyThatFile()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeQualityAsync(new QualityAnalysisRequest
        {
            Scope = AnalysisScope.All,
            Metrics = FileMetrics,
            ProjectPath = _projectPath,
            FilePath = _calculatorPath
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Complexity!.FileCount, Is.EqualTo(1));
        Assert.That(response.Complexity.Scope, Is.EqualTo(_calculatorPath));
        Assert.That(response.TestCoverage!.TotalFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task AnalyzeQuality_Duplicates_DetectedOnProjectPath()
    {
        var detector = Substitute.For<IDuplicateCodeDetectorService>();
        detector.DetectDuplicatesAsync(Arg.Any<string>(), Arg.Any<DuplicateDetectionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new DuplicateDetectionResult
            {
                DuplicateGroups = Array.Empty<MCPsharp.Models.DuplicateGroup>(),
                Metrics = new DuplicationMetrics
                {
                    TotalDuplicateGroups = 0,
                    ExactDuplicateGroups = 0,
                    NearMissDuplicateGroups = 0,
                    TotalDuplicateLines = 0,
                    DuplicationPercentage = 0,
                    FilesWithDuplicates = 0,
                    DuplicationByType = new Dictionary<DuplicateType, int>(),
                    DuplicationByFile = new Dictionary<string, FileDuplicationMetrics>(),
                    DuplicationBySimilarity = new Dictionary<string, int>(),
                    AverageSimilarity = 0,
                    MaxSimilarity = 0,
                    MinSimilarity = 0,
                    EstimatedMaintenanceCost = 0,
                    EstimatedRefactoringSavings = 0
                },
                AnalysisDuration = TimeSpan.Zero,
                FilesAnalyzed = 2,
                Warnings = Array.Empty<string>(),
                Success = true
            });
        var service = new UnifiedAnalysisService(duplicateDetector: detector);

        var response = await service.AnalyzeQualityAsync(new QualityAnalysisRequest
        {
            Scope = AnalysisScope.All,
            Metrics = new List<AnalysisQualityMetric> { AnalysisQualityMetric.Duplicates },
            ProjectPath = _projectPath,
            FilePath = _calculatorPath
        });

        Assert.That(response.Error, Is.Null);
        Assert.That(response.Duplicates, Is.Empty);
        await detector.Received(1).DetectDuplicatesAsync(_projectPath, Arg.Any<DuplicateDetectionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeQuality_WithoutPath_ReturnsError()
    {
        var service = new UnifiedAnalysisService();

        var response = await service.AnalyzeQualityAsync(new QualityAnalysisRequest
        {
            Scope = AnalysisScope.All,
            Metrics = FileMetrics
        });

        Assert.That(response.Error, Is.Not.Null.And.Contains("project path"));
    }

    #endregion
}
