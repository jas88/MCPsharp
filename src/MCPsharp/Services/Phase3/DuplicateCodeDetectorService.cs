using System.Collections.Concurrent;
using MCPsharp.Models;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Service for detecting duplicate code blocks and providing refactoring suggestions
/// </summary>
public partial class DuplicateCodeDetectorService : IDuplicateCodeDetectorService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<DuplicateCodeDetectorService> _logger;

    // Cache for extracted code blocks
    private readonly ConcurrentDictionary<string, List<CodeBlock>> _codeBlockCache = new();

    public DuplicateCodeDetectorService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ITypeUsageService typeUsage,
        ILogger<DuplicateCodeDetectorService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<DuplicateDetectionResult> DetectDuplicatesAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new DuplicateDetectionOptions();

        LogStartingDuplicateDetection(_logger, projectPath);

        try
        {
            // Initialize workspace if needed
            await EnsureWorkspaceInitialized(projectPath);

            // Extract code blocks from all files
            var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);

            // Find duplicates using different strategies
            var exactDuplicates = FindExactDuplicatesInternal(allCodeBlocks, options);
            var nearDuplicates = FindNearDuplicatesInternal(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

            // Combine and group duplicates
            var allDuplicateGroups = new List<DuplicateGroup>();
            allDuplicateGroups.AddRange(exactDuplicates);
            allDuplicateGroups.AddRange(nearDuplicates);
            RegisterGroups(allDuplicateGroups);

            // Calculate metrics
            var metrics = CalculateDuplicationMetrics(allDuplicateGroups, allCodeBlocks);

            // Generate refactoring suggestions
            var refactoringOptions = new RefactoringOptions
            {
                MaxSuggestions = 20
            };
            var refactoringSuggestions = GenerateRefactoringSuggestions(allDuplicateGroups, refactoringOptions, cancellationToken);

            // Get hotspots
            var hotspots = AnalyzeHotspots(allDuplicateGroups, allCodeBlocks);

            var duration = DateTime.UtcNow - startTime;

            LogDuplicateDetectionCompleted(_logger, duration.TotalMilliseconds, allDuplicateGroups.Count, metrics.FilesWithDuplicates);

            return new DuplicateDetectionResult
            {
                DuplicateGroups = allDuplicateGroups,
                Metrics = metrics,
                RefactoringSuggestions = refactoringSuggestions,
                Hotspots = hotspots,
                AnalysisDuration = duration,
                FilesAnalyzed = allCodeBlocks.Select(cb => cb.FilePath).Distinct().Count(),
                Warnings = new List<string>(),
                Success = true
            };
        }
        catch (Exception ex)
        {
            LogDuplicateDetectionError(_logger, ex);
            return new DuplicateDetectionResult
            {
                DuplicateGroups = new List<DuplicateGroup>(),
                Metrics = new DuplicationMetrics
                {
                    TotalDuplicateGroups = 0,
                    ExactDuplicateGroups = 0,
                    NearMissDuplicateGroups = 0,
                    TotalDuplicateLines = 0,
                    DuplicationPercentage = 0.0,
                    FilesWithDuplicates = 0,
                    DuplicationByType = new Dictionary<DuplicateType, int>(),
                    DuplicationByFile = new Dictionary<string, FileDuplicationMetrics>(),
                    DuplicationBySimilarity = new Dictionary<string, int>(),
                    AverageSimilarity = 0.0,
                    MaxSimilarity = 0.0,
                    MinSimilarity = 0.0,
                    EstimatedMaintenanceCost = 0.0,
                    EstimatedRefactoringSavings = 0.0
                },
                AnalysisDuration = DateTime.UtcNow - startTime,
                FilesAnalyzed = 0,
                Warnings = new List<string> { $"Analysis failed: {ex.Message}" },
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindExactDuplicatesAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        await EnsureWorkspaceInitialized(projectPath);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var groups = FindExactDuplicatesInternal(allCodeBlocks, options);
        RegisterGroups(groups);
        return groups;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
        string projectPath,
        double similarityThreshold = 0.8,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        options = options with { SimilarityThreshold = similarityThreshold };
        await EnsureWorkspaceInitialized(projectPath);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var groups = FindNearDuplicatesInternal(allCodeBlocks, similarityThreshold, options, cancellationToken);
        RegisterGroups(groups);
        return groups;
    }

    public async Task<DuplicationMetrics> AnalyzeDuplicationMetricsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        await EnsureWorkspaceInitialized(projectPath);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var exactDuplicates = FindExactDuplicatesInternal(allCodeBlocks, options);
        var nearDuplicates = FindNearDuplicatesInternal(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

        var allGroups = new List<DuplicateGroup>();
        allGroups.AddRange(exactDuplicates);
        allGroups.AddRange(nearDuplicates);

        return CalculateDuplicationMetrics(allGroups, allCodeBlocks);
    }

    public async Task<IReadOnlyList<RefactoringSuggestion>> GetRefactoringSuggestionsAsync(
        string projectPath,
        IReadOnlyList<DuplicateGroup> duplicates,
        RefactoringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(duplicates);
        options ??= new RefactoringOptions();
        await EnsureWorkspaceInitialized(projectPath);

        RegisterGroups(duplicates);
        return GenerateRefactoringSuggestions(duplicates.ToList(), options, cancellationToken);
    }

    public async Task<DuplicateDetectionResult> DetectDuplicatesInFilesAsync(
        IReadOnlyList<string> filePaths,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new DuplicateDetectionOptions();

        LogStartingFileDuplicateDetection(_logger, filePaths.Count);

        try
        {
            // Extract code blocks from specified files only
            var allCodeBlocks = new List<CodeBlock>();
            foreach (var filePath in filePaths)
            {
                var fileBlocks = await ExtractCodeBlocksFromFileAsync(filePath, options, cancellationToken);
                allCodeBlocks.AddRange(fileBlocks);
            }

            // Find duplicates
            var exactDuplicates = FindExactDuplicatesInternal(allCodeBlocks, options);
            var nearDuplicates = FindNearDuplicatesInternal(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

            var allDuplicateGroups = new List<DuplicateGroup>();
            allDuplicateGroups.AddRange(exactDuplicates);
            allDuplicateGroups.AddRange(nearDuplicates);
            RegisterGroups(allDuplicateGroups);

            var metrics = CalculateDuplicationMetrics(allDuplicateGroups, allCodeBlocks);

            return new DuplicateDetectionResult
            {
                DuplicateGroups = allDuplicateGroups,
                Metrics = metrics,
                AnalysisDuration = DateTime.UtcNow - startTime,
                FilesAnalyzed = filePaths.Count,
                Warnings = new List<string>(),
                Success = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFileDuplicateDetectionError(_logger, ex);
            throw new InvalidOperationException($"Duplicate detection in specific files failed: {ex.Message}", ex);
        }
    }

    public Task<CodeSimilarityResult> CompareCodeBlocksAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(CompareCodeBlocks(codeBlock1, codeBlock2, options ?? new CodeComparisonOptions()));
        }
        catch (Exception ex)
        {
            return Task.FromException<CodeSimilarityResult>(ex);
        }
    }

    private static CodeSimilarityResult CompareCodeBlocks(CodeBlock codeBlock1, CodeBlock codeBlock2, CodeComparisonOptions options)
    {
        // Structural (AST), token-category and normalised-token (semantic) similarity, weighted
        var similarity = ComputeSimilarity(
            codeBlock1, codeBlock2, options.StructuralWeight, options.TokenWeight, options.IgnoreIdentifiers, 0.0);

        var differences = FindCodeDifferences(codeBlock1, codeBlock2, similarity.Alignment);
        var commonPatterns = FindCommonPatterns(codeBlock1, codeBlock2);

        var isDuplicate = similarity.Overall >= DefaultDuplicateThreshold;
        var duplicateType = DeterminePairDuplicateType(codeBlock1, codeBlock2);

        return new CodeSimilarityResult
        {
            OverallSimilarity = similarity.Overall,
            StructuralSimilarity = similarity.Structural,
            TokenSimilarity = similarity.Token,
            SemanticSimilarity = similarity.Semantic,
            Differences = differences,
            CommonPatterns = commonPatterns,
            IsDuplicate = isDuplicate,
            DuplicateType = duplicateType,
            CloneType = DeterminePairCloneType(codeBlock1, codeBlock2, isDuplicate)
        };
    }

    public async Task<DuplicateHotspotsResult> GetDuplicateHotspotsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        await EnsureWorkspaceInitialized(projectPath);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var duplicates = await DetectDuplicatesAsync(projectPath, options, cancellationToken);

        return AnalyzeHotspots(duplicates.DuplicateGroups.ToList(), allCodeBlocks);
    }

    public async Task<RefactoringValidationResult> ValidateRefactoringAsync(
        string projectPath,
        RefactoringSuggestion suggestion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        await EnsureWorkspaceInitialized(projectPath);

        var issues = new List<ValidationIssue>();
        var dependencyImpacts = new List<DependencyImpact>();
        var riskLevel = RefactoringRisk.Low;

        // Validate extraction safety (data/control flow) and caller impact for each referenced group
        foreach (var groupId in suggestion.DuplicateGroupIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupRisk = ValidateGroupDependencies(groupId, suggestion, issues, dependencyImpacts, cancellationToken);
            if (groupRisk > riskLevel)
            {
                riskLevel = groupRisk;
            }
        }

        // Check for breaking changes
        if (suggestion.IsBreakingChange)
        {
            riskLevel = riskLevel > RefactoringRisk.High ? riskLevel : RefactoringRisk.High;
            issues.Add(new ValidationIssue
            {
                Type = ValidationIssueType.CompilationError,
                Severity = ValidationSeverity.Warning,
                Description = "This refactoring introduces breaking changes"
            });
        }

        // Add recommendations
        var recommendations = new List<string>();
        if (riskLevel >= RefactoringRisk.High)
        {
            recommendations.Add("Consider creating automated tests before refactoring");
            recommendations.Add("Implement this refactoring in small, incremental steps");
        }

        return new RefactoringValidationResult
        {
            IsValid = !issues.Exists(i => i.IsBlocking || i.Severity == ValidationSeverity.Critical),
            Issues = issues,
            DependencyImpacts = dependencyImpacts,
            OverallRisk = riskLevel,
            Recommendations = recommendations
        };
    }

    /// <summary>
    /// Load <paramref name="projectPath"/> into the shared workspace unless it already holds that project:
    /// reloading a real solution is expensive and discards state other tools rely on.
    /// </summary>
    private async Task EnsureWorkspaceInitialized(string projectPath)
    {
        if (_workspace.IsLoadedFrom(projectPath))
        {
            return;
        }

        try
        {
            await _workspace.InitializeAsync(projectPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWorkspaceInitializationFailed(_logger, ex, projectPath);
            throw new InvalidOperationException($"Failed to initialize Roslyn workspace for path '{projectPath}': {ex.Message}", ex);
        }
    }
}
