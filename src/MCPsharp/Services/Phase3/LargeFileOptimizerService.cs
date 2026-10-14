using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Phase3.LargeFileOptimization;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Service for analyzing and optimizing large C# files to improve code quality and maintainability
/// </summary>
public partial class LargeFileOptimizerService : ILargeFileOptimizerService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<LargeFileOptimizerService>? _logger;

    // Configuration constants
    private const int DefaultMaxLines = 500;
    private const int GodClassThreshold = 20; // methods
    private const int GodMethodThreshold = 50; // lines
    private const int MaxParameters = 7;
    private const int HighComplexityThreshold = 10;

    public LargeFileOptimizerService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ITypeUsageService typeUsage,
        ILogger<LargeFileOptimizerService>? logger = null)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<LargeFileAnalysisResult> AnalyzeLargeFilesAsync(string projectPath, int? maxLines = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var threshold = maxLines ?? DefaultMaxLines;
        var largeFiles = new List<LargeFileInfo>();
        var fileSizeDistribution = new Dictionary<string, int>();
        var filesAnalyzed = 0;
        var filesAboveThreshold = 0;
        var totalLines = 0;
        var largestFileSize = 0;
        var globalRecommendations = new List<OptimizationRecommendation>();

        if (!_workspace.IsInitialized)
        {
            return new LargeFileAnalysisResult
            {
                ProjectPath = projectPath,
                LargeFiles = largeFiles,
                FileSizeDistribution = fileSizeDistribution,
                AnalysisDuration = DateTime.UtcNow - startTime,
                TotalFilesAnalyzed = 0,
                FilesAboveThreshold = 0,
                AverageFileSize = 0,
                LargestFileSize = 0,
                GlobalRecommendations = globalRecommendations,
                Statistics = new OptimizationStatistics
                {
                    FilesNeedingOptimization = 0,
                    ClassesNeedingSplitting = 0,
                    MethodsNeedingRefactoring = 0,
                    GodClassesDetected = 0,
                    GodMethodsDetected = 0,
                    AverageComplexityScore = 0,
                    TotalCodeSmells = 0,
                    CodeSmellsBySeverity = new Dictionary<CodeSmellSeverity, int>(),
                    FilesByPriority = new Dictionary<OptimizationPriority, int>()
                }
            };
        }

        // Analyze each syntax tree
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (string.IsNullOrEmpty(syntaxTree.FilePath) || !syntaxTree.FilePath.EndsWith(".cs", StringComparison.Ordinal) || !IsFileInProject(syntaxTree.FilePath, projectPath))
                continue;

            filesAnalyzed++;
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var lineCount = root.GetText().Lines.Count;
            var characterCount = root.FullSpan.Length;

            totalLines += lineCount;
            largestFileSize = Math.Max(largestFileSize, lineCount);

            // Categorize file size
            var sizeCategory = CategorizeFileSize(lineCount);
            var categoryKey = sizeCategory.ToString();
            fileSizeDistribution[categoryKey] = fileSizeDistribution.GetValueOrDefault(categoryKey, 0) + 1;

            if (lineCount > threshold)
            {
                filesAboveThreshold++;

                // Analyze the large file
                var largeFile = await AnalyzeLargeFile(syntaxTree.FilePath, lineCount, characterCount, cancellationToken);
                if (largeFile != null)
                {
                    largeFiles.Add(largeFile);
                }
            }
        }

        // Generate global recommendations
        globalRecommendations = GenerateGlobalRecommendations(largeFiles, filesAnalyzed);

        // Calculate statistics
        var statistics = CalculateOptimizationStatistics(largeFiles);

        return new LargeFileAnalysisResult
        {
            ProjectPath = projectPath,
            LargeFiles = largeFiles.OrderByDescending(f => f.LineCount).ToList(),
            FileSizeDistribution = fileSizeDistribution,
            AnalysisDuration = DateTime.UtcNow - startTime,
            TotalFilesAnalyzed = filesAnalyzed,
            FilesAboveThreshold = filesAboveThreshold,
            AverageFileSize = filesAnalyzed > 0 ? totalLines / filesAnalyzed : 0,
            LargestFileSize = largestFileSize,
            GlobalRecommendations = globalRecommendations,
            Statistics = statistics
        };
    }

    public async Task<ClassOptimizationResult> OptimizeLargeClassAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            throw new FileNotFoundException($"File not found: {filePath}");

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null || semanticModel == null)
            throw new InvalidOperationException($"Unable to get syntax tree or semantic model for {filePath}");

        var root = await syntaxTree.GetRootAsync(cancellationToken);

        // Find the largest class in the file
        var classDeclarations = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .OrderByDescending(c => c.GetText().Lines.Count)
            .ToList();

        if (classDeclarations.Count == 0)
            throw new InvalidOperationException("No classes found in the specified file");

        var targetClass = classDeclarations[0];
        var classSymbol = semanticModel.GetDeclaredSymbol(targetClass, cancellationToken);
        if (classSymbol == null)
            throw new InvalidOperationException("Could not get semantic information for the class");

        // Analyze current metrics
        var currentMetrics = AnalyzeClassMetrics(targetClass, semanticModel, cancellationToken);

        // Generate splitting strategies
        var splittingStrategies = GenerateSplittingStrategies(targetClass, currentMetrics);

        // Generate refactoring suggestions
        var refactoringSuggestions = GenerateClassRefactoringSuggestions(targetClass, currentMetrics);

        // Generate before/after examples
        var beforeAfterExamples = GenerateClassBeforeAfterExamples(splittingStrategies);

        // Calculate priority and effort
        var priority = CalculateOptimizationPriority(currentMetrics);
        var estimatedEffort = EstimateEffort(splittingStrategies, refactoringSuggestions);
        var expectedBenefit = CalculateExpectedBenefit(currentMetrics, splittingStrategies);

        return new ClassOptimizationResult
        {
            ClassName = targetClass.Identifier.Text,
            FilePath = filePath,
            CurrentMetrics = currentMetrics,
            SplittingStrategies = splittingStrategies,
            RefactoringSuggestions = refactoringSuggestions,
            BeforeAfterExamples = beforeAfterExamples,
            Priority = priority,
            RecommendedActions = GenerateRecommendedActions(splittingStrategies, refactoringSuggestions),
            EstimatedEffortHours = estimatedEffort,
            ExpectedBenefit = expectedBenefit
        };
    }

    public async Task<MethodOptimizationResult> OptimizeLargeMethodAsync(string filePath, string methodName, CancellationToken cancellationToken = default)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            throw new FileNotFoundException($"File not found: {filePath}");

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null || semanticModel == null)
            throw new InvalidOperationException($"Unable to get syntax tree or semantic model for file: {filePath}");
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        // Find the target method
        var methodDeclarations = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (methodDeclarations.Count == 0)
            throw new InvalidOperationException($"Method '{methodName}' not found in the specified file");

        var targetMethod = methodDeclarations[0];
        var containingClass = targetMethod.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass == null)
            throw new InvalidOperationException("Method must be within a class");

        // Analyze current metrics
        var currentMetrics = AnalyzeMethodMetrics(targetMethod);

        // Generate refactoring strategies
        var refactoringStrategies = GenerateMethodRefactoringStrategies(currentMetrics);

        // Generate before/after examples
        var beforeAfterExamples = GenerateMethodBeforeAfterExamples(refactoringStrategies);

        // Calculate priority and effort
        var priority = CalculateMethodOptimizationPriority(currentMetrics);
        var estimatedEffort = EstimateMethodEffort(refactoringStrategies);
        var expectedBenefit = CalculateMethodExpectedBenefit(currentMetrics, refactoringStrategies);
        var lineSpan = targetMethod.GetLocation().GetLineSpan();

        return new MethodOptimizationResult
        {
            MethodName = methodName,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            ClassName = containingClass.Identifier.Text,
            FilePath = filePath,
            CurrentMetrics = currentMetrics,
            RefactoringStrategies = refactoringStrategies,
            BeforeAfterExamples = beforeAfterExamples,
            Priority = priority,
            EstimatedEffortHours = estimatedEffort,
            ExpectedBenefit = expectedBenefit
        };
    }

    public async Task<ComplexityMetrics> GetComplexityMetricsAsync(string filePath, string? methodName = null, CancellationToken cancellationToken = default)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            throw new FileNotFoundException($"File not found: {filePath}");

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null || semanticModel == null)
            return null!;
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        if (!string.IsNullOrEmpty(methodName))
        {
            // Analyze a specific member: methods first, then constructors, accessors and other function-like members
            SyntaxNode? member = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text.Equals(methodName, StringComparison.OrdinalIgnoreCase));
            member ??= root.DescendantNodes()
                .Where(CodeComplexityCalculator.IsMeasuredMember)
                .FirstOrDefault(n => CodeComplexityCalculator.GetMemberName(n).Equals(methodName, StringComparison.OrdinalIgnoreCase));

            if (member != null)
            {
                return CalculateMethodComplexity(member);
            }
        }

        // Analyze entire file
        return CalculateFileComplexity(root, cancellationToken);
    }

    public async Task<OptimizationPlan> GenerateOptimizationPlanAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Get comprehensive analysis
        var largeFileResult = await AnalyzeLargeFilesAsync(Path.GetDirectoryName(filePath)!, cancellationToken: cancellationToken);
        var largeFile = largeFileResult.LargeFiles.FirstOrDefault(f => f.FilePath == filePath);

        if (largeFile == null)
        {
            throw new InvalidOperationException($"File '{filePath}' is not considered large and doesn't require optimization");
        }

        var actions = new List<OptimizationAction>();
        var totalEffort = 0;
        var totalBenefit = 0.0;

        // Generate actions for each type of optimization needed
        if (largeFile.LargeClasses.Count != 0)
        {
            foreach (var className in largeFile.LargeClasses)
            {
                var classOptimization = await OptimizeLargeClassAsync(filePath, cancellationToken);
                actions.AddRange(ConvertToOptimizationActions(classOptimization));
                totalEffort += classOptimization.EstimatedEffortHours;
                totalBenefit += classOptimization.ExpectedBenefit;
            }
        }

        if (largeFile.LargeMethods.Count > 0)
        {
            foreach (var methodEntry in largeFile.LargeMethods)
            {
                // Entries are formatted "Name (N lines)"
                var separator = methodEntry.IndexOf(" (", StringComparison.Ordinal);
                var methodName = separator > 0 ? methodEntry[..separator] : methodEntry;
                var methodOptimization = await OptimizeLargeMethodAsync(filePath, methodName, cancellationToken);
                actions.AddRange(ConvertToOptimizationActions(methodOptimization));
                totalEffort += methodOptimization.EstimatedEffortHours;
                totalBenefit += methodOptimization.ExpectedBenefit;
            }
        }

        // Add complexity hotspot fixes
        foreach (var action in ConvertHotspotsToActions(filePath, largeFile.OverallComplexity.Hotspots))
        {
            actions.Add(action);
            totalEffort += action.EstimatedEffortHours;
            totalBenefit += action.ExpectedBenefit;
        }

        // Add code smell fixes
        foreach (var codeSmell in largeFile.CodeSmells)
        {
            var action = ConvertCodeSmellToAction(codeSmell);
            actions.Add(action);
            totalEffort += action.EstimatedEffortHours;
            totalBenefit += action.ExpectedBenefit;
        }

        // Sort actions by priority and benefit/effort ratio
        actions = actions
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.ExpectedBenefit / Math.Max(1, a.EstimatedEffortHours))
            .ToList();

        return new OptimizationPlan
        {
            FilePath = filePath,
            CreatedAt = startTime,
            Actions = actions,
            OverallPriority = DetermineOverallPriority(largeFile),
            TotalEstimatedEffortHours = totalEffort,
            TotalExpectedBenefit = totalBenefit,
            Prerequisites = GeneratePrerequisites(actions),
            Risks = GenerateRisks(actions),
            Recommendations = GeneratePlanRecommendations()
        };
    }

    public async Task<List<GodClassAnalysis>> DetectGodClassesAsync(string projectPath, string? filePath = null, CancellationToken cancellationToken = default)
    {
        var godClasses = new List<GodClassAnalysis>();
        if (!_workspace.IsInitialized)
            return godClasses;

        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (!syntaxTree.FilePath?.EndsWith(".cs", StringComparison.Ordinal) == true)
                continue;
            if (!string.IsNullOrEmpty(filePath) && syntaxTree.FilePath != filePath)
                continue;
            if (syntaxTree.FilePath == null || !IsFileInProject(syntaxTree.FilePath, projectPath))
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var classDecl in classDeclarations)
            {
                var godClassAnalysis = AnalyzeGodClass(classDecl, semanticModel, syntaxTree.FilePath);
                if (godClassAnalysis.Severity != GodClassSeverity.None)
                {
                    godClasses.Add(godClassAnalysis);
                }
            }
        }

        return godClasses.OrderByDescending(gc => gc.GodClassScore).ToList();
    }

    public async Task<List<GodMethodAnalysis>> DetectGodMethodsAsync(string projectPath, string? filePath = null, CancellationToken cancellationToken = default)
    {
        var godMethods = new List<GodMethodAnalysis>();
        if (!_workspace.IsInitialized)
            return godMethods;

        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (!syntaxTree.FilePath?.EndsWith(".cs", StringComparison.Ordinal) == true)
                continue;
            if (!string.IsNullOrEmpty(filePath) && syntaxTree.FilePath != filePath)
                continue;
            if (syntaxTree.FilePath == null || !IsFileInProject(syntaxTree.FilePath, projectPath))
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var godMethodAnalysis = AnalyzeGodMethod(methodDecl, syntaxTree.FilePath);
                if (godMethodAnalysis.Severity != GodMethodSeverity.None)
                {
                    godMethods.Add(godMethodAnalysis);
                }
            }
        }

        return godMethods.OrderByDescending(gm => gm.GodMethodScore).ToList();
    }

    public async Task<List<CodeSmell>> AnalyzeCodeSmellsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            throw new FileNotFoundException($"File not found: {filePath}");

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null)
            throw new InvalidOperationException($"Unable to get syntax tree for file: {filePath}");
        if (semanticModel == null)
            throw new InvalidOperationException($"Unable to get semantic model for file: {filePath}");

        var root = await syntaxTree.GetRootAsync(cancellationToken);

        return LargeFileOptimization.CodeSmellDetector.AnalyzeCodeSmells(root, semanticModel, filePath, cancellationToken);
    }

    public async Task<List<RefactoringSuggestion>> SuggestRefactoringPatternsAsync(string filePath, List<CodeSmell> issues, CancellationToken cancellationToken = default)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
            throw new FileNotFoundException($"File not found: {filePath}");

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (syntaxTree == null)
            throw new InvalidOperationException($"Unable to get syntax tree for file: {filePath}");
        if (semanticModel == null)
            throw new InvalidOperationException($"Unable to get semantic model for file: {filePath}");

        var suggestions = new List<RefactoringSuggestion>();

        foreach (var issue in issues)
        {
            var suggestion = GenerateRefactoringSuggestion(issue);
            if (suggestion != null)
            {
                suggestions.Add(suggestion);
            }
        }

        return suggestions.OrderByDescending(s => s.Confidence).ToList();
    }

    public Task<OptimizationEstimate> EstimateOptimizationEffortAsync(OptimizationPlan optimizationPlan, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(EstimateOptimizationEffort(optimizationPlan));
        }
        catch (Exception ex)
        {
            return Task.FromException<OptimizationEstimate>(ex);
        }
    }
}
