using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Tests.Services.Phase3;

/// <summary>
/// End-to-end tests of Type-2/Type-3 clone detection and refactoring dependency validation
/// </summary>
[TestFixture]
public class DuplicateCodeDetectorServiceTests
{
    private static readonly string[] RenamedMethodNames = ["Sum", "Accumulate"];
    private static readonly string[] NearMissTypeNames = ["Gamma", "Delta"];

    private const string RenamedClones = """
        namespace Sample;

        public class Alpha
        {
            public int Sum(int[] values)
            {
                var total = 0;
                foreach (var value in values)
                {
                    total += value;
                }
                return total;
            }
        }

        public class Beta
        {
            public int Accumulate(int[] items)
            {
                var acc = 0;
                foreach (var item in items)
                {
                    acc += item;
                }
                return acc;
            }
        }
        """;

    private const string NearMissClones = """
        namespace Sample;

        public class Gamma
        {
            public int Sum(int[] values)
            {
                var total = 0;
                foreach (var value in values)
                {
                    total += value;
                }
                return total;
            }
        }

        public class Delta
        {
            public int Sum(int[] values)
            {
                var total = 0;
                foreach (var value in values)
                {
                    total += value;
                }
                total = total * 2;
                return total;
            }
        }
        """;

    private string _directory = null!;
    private RoslynWorkspace _workspace = null!;
    private DuplicateCodeDetectorService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mcpsharp-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _workspace = new RoslynWorkspace();
        var symbolQuery = new SymbolQueryService(_workspace);
        _service = new DuplicateCodeDetectorService(
            _workspace,
            symbolQuery,
            new CallerAnalysisService(_workspace, symbolQuery),
            new TypeUsageService(_workspace, symbolQuery),
            NullLogger<DuplicateCodeDetectorService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task DetectDuplicatesInFiles_DetectsRenamedVariableCloneAsSemanticDuplicate()
    {
        var file = await WriteSourceAsync("Renamed.cs", RenamedClones);

        var result = await _service.DetectDuplicatesInFilesAsync([file], MethodsOnly());

        var group = result.DuplicateGroups.SingleOrDefault(g => g.CloneType == CloneType.Renamed);
        Assert.That(group, Is.Not.Null, "renamed clone group expected");
        Assert.That(group!.IsExactDuplicate, Is.False);
        Assert.That(group.CodeBlocks.Select(b => b.ElementName), Is.EquivalentTo(RenamedMethodNames));
        Assert.That(group.SimilarityScore, Is.GreaterThanOrEqualTo(0.95));
    }

    [Test]
    public async Task DetectDuplicatesInFiles_DetectsNearMissCloneWithAddedStatement()
    {
        var file = await WriteSourceAsync("NearMiss.cs", NearMissClones);

        var result = await _service.DetectDuplicatesInFilesAsync([file], MethodsOnly());

        var group = result.DuplicateGroups.SingleOrDefault(g => g.CloneType == CloneType.NearMiss);
        Assert.That(group, Is.Not.Null, "near-miss clone group expected");
        Assert.That(group!.CodeBlocks.Select(b => b.ContainingType), Is.EquivalentTo(NearMissTypeNames));
        Assert.That(group.SimilarityScore, Is.InRange(0.8, 0.999));

        var comparison = await _service.CompareCodeBlocksAsync(group.CodeBlocks[0], group.CodeBlocks[1]);
        Assert.That(comparison.CloneType, Is.EqualTo(CloneType.NearMiss));
        Assert.That(comparison.IsDuplicate, Is.True);
        var added = comparison.Differences.Where(d => d.Type is DifferenceType.StatementAdded or DifferenceType.StatementRemoved).ToList();
        Assert.That(added, Has.Count.EqualTo(1));
        Assert.That(added[0].Text1 + added[0].Text2, Is.EqualTo("total = total * 2;"));
    }

    [Test]
    public async Task DetectDuplicates_InProject_AttachesSemanticExtractionSafetyAndValidates()
    {
        await WriteSourceAsync("Renamed.cs", RenamedClones);

        var result = await _service.DetectDuplicatesAsync(_directory, MethodsOnly());

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var suggestion = result.RefactoringSuggestions?.FirstOrDefault(s => s.RefactoringType == RefactoringType.ExtractMethod);
        Assert.That(suggestion, Is.Not.Null);
        Assert.That(suggestion!.ExtractionSafety, Is.Not.Null);
        Assert.That(suggestion.ExtractionSafety!.Blocks.Select(b => b.Mode), Is.All.EqualTo(ExtractionAnalysisMode.Semantic));
        Assert.That(suggestion.ExtractionSafety.IsSafe, Is.True, string.Join("; ", suggestion.ExtractionSafety.UnsafeReasons));

        var validation = await _service.ValidateRefactoringAsync(_directory, suggestion);

        Assert.That(validation.IsValid, Is.True);
        Assert.That(validation.Issues.Select(i => i.Description), Has.None.Contains("not known to this session"));
    }

    [Test]
    public async Task ValidateRefactoring_UnknownGroup_ReportsWarning()
    {
        await WriteSourceAsync("Renamed.cs", RenamedClones);
        var suggestion = new RefactoringSuggestion
        {
            SuggestionId = "s1",
            RefactoringType = RefactoringType.ExtractMethod,
            Title = "Extract",
            Description = "Extract",
            DuplicateGroupIds = ["missing-group"],
            Priority = RefactoringPriority.Low,
            EstimatedEffort = 0,
            EstimatedBenefit = 0,
            Risk = RefactoringRisk.Low,
            IsBreakingChange = false,
            ImplementationSteps = [],
            Prerequisites = [],
            SideEffects = [],
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "Test",
                Confidence = 1.0,
                AdditionalMetadata = new Dictionary<string, object>()
            }
        };

        var validation = await _service.ValidateRefactoringAsync(_directory, suggestion);

        Assert.That(validation.Issues.Select(i => i.Description), Has.Some.Contains("not known to this session"));
        Assert.That(validation.DependencyImpacts, Is.Empty);
    }

    private static DuplicateDetectionOptions MethodsOnly() => new()
    {
        DetectionTypes = DuplicateDetectionTypes.Methods,
        MinBlockSize = 3,
        SimilarityThreshold = 0.8
    };

    private async Task<string> WriteSourceAsync(string fileName, string source)
    {
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(path, source);
        return path;
    }
}
