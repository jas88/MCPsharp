using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;

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

    #region Private Implementation Methods

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

    private async Task<List<CodeBlock>> ExtractAllCodeBlocksAsync(
        string projectPath,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{projectPath}:{GetOptionsHash(options)}";

        if (_codeBlockCache.TryGetValue(cacheKey, out var cachedBlocks))
        {
            return cachedBlocks;
        }

        var allCodeBlocks = new List<CodeBlock>();
        if (!_workspace.IsInitialized)
        {
            LogNoCompilation(_logger);
            return allCodeBlocks;
        }

        // Process each syntax tree
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var filePath = syntaxTree.FilePath;
            if (ShouldExcludeFile(filePath, options))
                continue;

            try
            {
                var fileBlocks = await ExtractCodeBlocksFromSyntaxTreeAsync(syntaxTree, options, cancellationToken);
                allCodeBlocks.AddRange(fileBlocks);
            }
            catch (Exception ex)
            {
                LogCodeBlockExtractionFailed(_logger, ex, filePath);
            }
        }

        _codeBlockCache.TryAdd(cacheKey, allCodeBlocks);
        return allCodeBlocks;
    }

    private async Task<List<CodeBlock>> ExtractCodeBlocksFromSyntaxTreeAsync(
        SyntaxTree syntaxTree,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var semanticModel = _workspace.GetSemanticModel(syntaxTree);

        if (semanticModel == null)
            return new List<CodeBlock>();

        return ExtractCodeBlocksFromRoot(root, syntaxTree.FilePath, options, new FingerprintContext(semanticModel));
    }

    private static List<CodeBlock> ExtractCodeBlocksFromRoot(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var codeBlocks = new List<CodeBlock>();

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Methods))
        {
            codeBlocks.AddRange(ExtractMethodBlocks(root, filePath, options, fingerprints));
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Classes))
        {
            codeBlocks.AddRange(ExtractClassBlocks(root, filePath, options, fingerprints));
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.CodeBlocks))
        {
            codeBlocks.AddRange(ExtractCodeBlockStatements(root, filePath, options, fingerprints));
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Properties))
        {
            codeBlocks.AddRange(ExtractPropertyBlocks(root, filePath, options, fingerprints));
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Constructors))
        {
            codeBlocks.AddRange(ExtractConstructorBlocks(root, filePath, options, fingerprints));
        }

        return codeBlocks;
    }

    private static List<CodeBlock> ExtractMethodBlocks(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var methodBlocks = new List<CodeBlock>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            if (method.Body == null) continue; // Skip abstract methods

            var lineSpan = method.SyntaxTree.GetLineSpan(method.Span);
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lineCount < options.MinBlockSize || lineCount > options.MaxBlockSize)
                continue;

            var sourceCode = method.ToFullString();
            var normalizedCode = NormalizeCode(sourceCode, options);
            var codeHash = ComputeHash(normalizedCode);

            // Extract complexity metrics
            var complexity = CalculateComplexity(method);

            // Extract tokens
            var tokens = ExtractTokens(method);

            // Extract AST structure
            var astStructure = ExtractAstStructure(method);

            // Determine if it's generated or test code
            var isGenerated = IsGeneratedCode(method);
            var isTestCode = IsTestCode(filePath);

            if ((options.IgnoreGeneratedCode && isGenerated) ||
                (options.IgnoreTestCode && isTestCode))
                continue;

            methodBlocks.Add(new CodeBlock
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                SourceCode = sourceCode,
                NormalizedCode = normalizedCode,
                CodeHash = codeHash,
                ElementType = CodeElementType.Method,
                ElementName = method.Identifier.Text,
                ContainingType = GetContainingType(method),
                Namespace = GetNamespace(method),
                Accessibility = GetAccessibility(method),
                IsGenerated = isGenerated,
                IsTestCode = isTestCode,
                Complexity = complexity,
                Tokens = tokens,
                AstStructure = astStructure,
                Context = ExtractContext(method, options.ContextLines),
                SpanStart = method.Span.Start,
                SpanLength = method.Span.Length,
                Fingerprint = fingerprints.Build(method)
            });
        }

        return methodBlocks;
    }

    private static List<CodeBlock> ExtractClassBlocks(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var classBlocks = new List<CodeBlock>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var lineSpan = classDecl.SyntaxTree.GetLineSpan(classDecl.Span);
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lineCount < options.MinBlockSize || lineCount > options.MaxBlockSize)
                continue;

            var sourceCode = classDecl.ToFullString();
            var normalizedCode = NormalizeCode(sourceCode, options);
            var codeHash = ComputeHash(normalizedCode);

            var complexity = CalculateComplexity(classDecl);
            var tokens = ExtractTokens(classDecl);
            var astStructure = ExtractAstStructure(classDecl);
            var isGenerated = IsGeneratedCode(classDecl);
            var isTestCode = IsTestCode(filePath);

            if ((options.IgnoreGeneratedCode && isGenerated) ||
                (options.IgnoreTestCode && isTestCode))
                continue;

            classBlocks.Add(new CodeBlock
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                SourceCode = sourceCode,
                NormalizedCode = normalizedCode,
                CodeHash = codeHash,
                ElementType = CodeElementType.Class,
                ElementName = classDecl.Identifier.Text,
                Namespace = GetNamespace(classDecl),
                Accessibility = GetAccessibility(classDecl),
                IsGenerated = isGenerated,
                IsTestCode = isTestCode,
                Complexity = complexity,
                Tokens = tokens,
                AstStructure = astStructure,
                Context = ExtractContext(classDecl, options.ContextLines),
                SpanStart = classDecl.Span.Start,
                SpanLength = classDecl.Span.Length,
                Fingerprint = fingerprints.Build(classDecl)
            });
        }

        return classBlocks;
    }

    private static List<CodeBlock> ExtractCodeBlockStatements(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var blockBlocks = new List<CodeBlock>();

        // Extract blocks within methods that are at least the minimum size
        var methodBodies = root.DescendantNodes().OfType<BlockSyntax>();

        foreach (var block in methodBodies)
        {
            var lineSpan = block.SyntaxTree.GetLineSpan(block.Span);
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lineCount < options.MinBlockSize || lineCount > options.MaxBlockSize)
                continue;

            var sourceCode = block.ToFullString();
            var normalizedCode = NormalizeCode(sourceCode, options);
            var codeHash = ComputeHash(normalizedCode);

            var complexity = CalculateComplexity(block);
            var tokens = ExtractTokens(block);
            var astStructure = ExtractAstStructure(block);

            var containingMethod = block.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            var isGenerated = containingMethod != null && IsGeneratedCode(containingMethod);
            var isTestCode = IsTestCode(filePath);

            if ((options.IgnoreGeneratedCode && isGenerated) ||
                (options.IgnoreTestCode && isTestCode))
                continue;

            blockBlocks.Add(new CodeBlock
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                SourceCode = sourceCode,
                NormalizedCode = normalizedCode,
                CodeHash = codeHash,
                ElementType = CodeElementType.CodeBlock,
                ElementName = $"Block_{lineSpan.StartLinePosition.Line + 1}",
                ContainingType = containingMethod != null ? GetContainingType(containingMethod) : null,
                Namespace = containingMethod != null ? GetNamespace(containingMethod) : null,
                Accessibility = containingMethod != null ? GetAccessibility(containingMethod) : MCPsharp.Models.Accessibility.Private,
                IsGenerated = isGenerated,
                IsTestCode = isTestCode,
                Complexity = complexity,
                Tokens = tokens,
                AstStructure = astStructure,
                Context = ExtractContext(block, options.ContextLines),
                SpanStart = block.Span.Start,
                SpanLength = block.Span.Length,
                Fingerprint = fingerprints.Build(block)
            });
        }

        return blockBlocks;
    }

    private static List<CodeBlock> ExtractPropertyBlocks(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var propertyBlocks = new List<CodeBlock>();
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        foreach (var property in properties)
        {
            if (property.AccessorList == null) continue;

            var lineSpan = property.SyntaxTree.GetLineSpan(property.Span);
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lineCount < options.MinBlockSize || lineCount > options.MaxBlockSize)
                continue;

            var sourceCode = property.ToFullString();
            var normalizedCode = NormalizeCode(sourceCode, options);
            var codeHash = ComputeHash(normalizedCode);

            var complexity = CalculateComplexity(property);
            var tokens = ExtractTokens(property);
            var astStructure = ExtractAstStructure(property);
            var isGenerated = IsGeneratedCode(property);
            var isTestCode = IsTestCode(filePath);

            if ((options.IgnoreGeneratedCode && isGenerated) ||
                (options.IgnoreTestCode && isTestCode))
                continue;

            propertyBlocks.Add(new CodeBlock
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                SourceCode = sourceCode,
                NormalizedCode = normalizedCode,
                CodeHash = codeHash,
                ElementType = CodeElementType.Property,
                ElementName = property.Identifier.Text,
                ContainingType = GetContainingType(property),
                Namespace = GetNamespace(property),
                Accessibility = GetAccessibility(property),
                IsGenerated = isGenerated,
                IsTestCode = isTestCode,
                Complexity = complexity,
                Tokens = tokens,
                AstStructure = astStructure,
                Context = ExtractContext(property, options.ContextLines),
                SpanStart = property.Span.Start,
                SpanLength = property.Span.Length,
                Fingerprint = fingerprints.Build(property)
            });
        }

        return propertyBlocks;
    }

    private static List<CodeBlock> ExtractConstructorBlocks(
        SyntaxNode root,
        string filePath,
        DuplicateDetectionOptions options,
        FingerprintContext fingerprints)
    {
        var constructorBlocks = new List<CodeBlock>();
        var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

        foreach (var constructor in constructors)
        {
            if (constructor.Body == null) continue;

            var lineSpan = constructor.SyntaxTree.GetLineSpan(constructor.Span);
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lineCount < options.MinBlockSize || lineCount > options.MaxBlockSize)
                continue;

            var sourceCode = constructor.ToFullString();
            var normalizedCode = NormalizeCode(sourceCode, options);
            var codeHash = ComputeHash(normalizedCode);

            var complexity = CalculateComplexity(constructor);
            var tokens = ExtractTokens(constructor);
            var astStructure = ExtractAstStructure(constructor);
            var isGenerated = IsGeneratedCode(constructor);
            var isTestCode = IsTestCode(filePath);

            if ((options.IgnoreGeneratedCode && isGenerated) ||
                (options.IgnoreTestCode && isTestCode))
                continue;

            constructorBlocks.Add(new CodeBlock
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1,
                SourceCode = sourceCode,
                NormalizedCode = normalizedCode,
                CodeHash = codeHash,
                ElementType = CodeElementType.Constructor,
                ElementName = ".ctor",
                ContainingType = GetContainingType(constructor),
                Namespace = GetNamespace(constructor),
                Accessibility = GetAccessibility(constructor),
                IsGenerated = isGenerated,
                IsTestCode = isTestCode,
                Complexity = complexity,
                Tokens = tokens,
                AstStructure = astStructure,
                Context = ExtractContext(constructor, options.ContextLines),
                SpanStart = constructor.Span.Start,
                SpanLength = constructor.Span.Length,
                Fingerprint = fingerprints.Build(constructor)
            });
        }

        return constructorBlocks;
    }

    private async Task<List<CodeBlock>> ExtractCodeBlocksFromFileAsync(
        string filePath,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            // Prefer the workspace's tree (which has a semantic model); otherwise parse the file (syntax only)
            var compilation = _workspace.GetCompilationForFile(filePath);
            var workspaceTree = compilation?.SyntaxTrees.FirstOrDefault(t => PathsEqual(t.FilePath, filePath));
            if (compilation != null && workspaceTree != null)
            {
                var workspaceRoot = await workspaceTree.GetRootAsync(cancellationToken);
                return ExtractCodeBlocksFromRoot(workspaceRoot, filePath, options,
                    new FingerprintContext(compilation.GetSemanticModel(workspaceTree)));
            }

            var sourceText = await File.ReadAllTextAsync(filePath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath, cancellationToken: cancellationToken);
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            return ExtractCodeBlocksFromRoot(root, filePath, options, new FingerprintContext(null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCodeBlockExtractionFailed(_logger, ex, filePath);
            return new List<CodeBlock>();
        }
    }

    private static List<DuplicateGroup> FindExactDuplicatesInternal(
        List<CodeBlock> codeBlocks,
        DuplicateDetectionOptions options)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var hashGroups = codeBlocks.GroupBy(cb => cb.CodeHash);

        foreach (var hashGroup in hashGroups)
        {
            if (hashGroup.Count() < 2) continue; // Need at least 2 blocks to be a duplicate

            var blocksList = hashGroup.ToList();
            var similarityScore = 1.0; // Exact duplicates have 100% similarity
            var lineCount = blocksList[0].EndLine - blocksList[0].StartLine + 1;
            var complexity = blocksList[0].Complexity.OverallScore;
            var duplicateType = DetermineDuplicateType(blocksList[0]);

            duplicateGroups.Add(new DuplicateGroup
            {
                GroupId = Guid.NewGuid().ToString(),
                CodeBlocks = blocksList,
                SimilarityScore = similarityScore,
                DuplicationType = duplicateType,
                LineCount = lineCount,
                Complexity = (int)complexity,
                IsExactDuplicate = true,
                CloneType = CloneType.Exact,
                Impact = CalculateDuplicationImpact(blocksList, similarityScore),
                Metadata = new DuplicateMetadata
                {
                    DetectedAt = DateTime.UtcNow,
                    DetectionAlgorithm = "ExactHashMatch",
                    DetectionConfiguration = GetOptionsHash(options),
                    AdditionalMetadata = new Dictionary<string, object>
                    {
                        ["HashAlgorithm"] = "SHA256",
                        ["NormalizationLevel"] = options.IgnoreTrivialDifferences ? "Full" : "Partial"
                    }
                }
            });
        }

        return duplicateGroups.OrderByDescending(g => g.Complexity).ToList();
    }

    private static List<CodeDifference> FindCodeDifferences(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CloneAlignment? alignment)
    {
        // Token-level diff over normalised tokens, with statement-level grouping of insertions/deletions
        if (codeBlock1.Fingerprint != null && codeBlock2.Fingerprint != null)
        {
            alignment ??= CloneComparer.Align(codeBlock1.Fingerprint, codeBlock2.Fingerprint);
            if (alignment != null)
            {
                return CloneDiffer.FindDifferences(codeBlock1.Fingerprint, codeBlock2.Fingerprint, alignment).ToList();
            }
        }

        // Fingerprints unavailable or the blocks are too different to align: report a single coarse difference
        var differences = new List<CodeDifference>();
        if (codeBlock1.NormalizedCode != codeBlock2.NormalizedCode)
        {
            differences.Add(new CodeDifference
            {
                Type = DifferenceType.Structural,
                Description = "Code blocks have different structure",
                Location1 = new CodeLocation { Line = codeBlock1.StartLine, Column = codeBlock1.StartColumn, Length = codeBlock1.EndLine - codeBlock1.StartLine + 1 },
                Location2 = new CodeLocation { Line = codeBlock2.StartLine, Column = codeBlock2.StartColumn, Length = codeBlock2.EndLine - codeBlock2.StartLine + 1 },
                Text1 = codeBlock1.NormalizedCode,
                Text2 = codeBlock2.NormalizedCode,
                Impact = CloneDiffer.StructuralDifferenceImpact
            });
        }

        return differences;
    }

    private static List<CommonPattern> FindCommonPatterns(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2)
    {
        var patterns = new List<CommonPattern>();

        // Analyze control flow patterns
        var commonFlowPatterns = codeBlock1.AstStructure.ControlFlowPatterns
            .Intersect(codeBlock2.AstStructure.ControlFlowPatterns)
            .ToList();

        foreach (var pattern in commonFlowPatterns)
        {
            patterns.Add(new CommonPattern
            {
                Type = PatternType.ControlFlow,
                Description = $"Common {pattern} pattern",
                Frequency = 1,
                Confidence = 0.8
            });
        }

        // Analyze data flow patterns
        var commonDataPatterns = codeBlock1.AstStructure.DataFlowPatterns
            .Intersect(codeBlock2.AstStructure.DataFlowPatterns)
            .ToList();

        foreach (var pattern in commonDataPatterns)
        {
            patterns.Add(new CommonPattern
            {
                Type = PatternType.DataFlow,
                Description = $"Common {pattern} pattern",
                Frequency = 1,
                Confidence = 0.7
            });
        }

        return patterns;
    }

    private static DuplicateType? DeterminePairDuplicateType(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2)
    {
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return null;

        return codeBlock1.ElementType switch
        {
            CodeElementType.Method => DuplicateType.Method,
            CodeElementType.Class => DuplicateType.Class,
            CodeElementType.Property => DuplicateType.Property,
            CodeElementType.Constructor => DuplicateType.Constructor,
            CodeElementType.CodeBlock => DuplicateType.CodeBlock,
            _ => DuplicateType.Unknown
        };
    }

    private static DuplicationMetrics CalculateDuplicationMetrics(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks)
    {
        var filesWithDuplicates = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .Select(cb => cb.FilePath)
            .Distinct()
            .Count();

        var totalLines = allCodeBlocks.Sum(cb => cb.EndLine - cb.StartLine + 1);
        var duplicateLines = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .Sum(cb => cb.EndLine - cb.StartLine + 1);

        var duplicationPercentage = totalLines > 0 ? (double)duplicateLines / totalLines * 100 : 0.0;

        // Calculate duplication by type
        var duplicationByType = duplicateGroups
            .GroupBy(g => g.DuplicationType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate duplication by file
        var duplicationByFile = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .GroupBy(cb => cb.FilePath)
            .ToDictionary(
                g => g.Key,
                g => new FileDuplicationMetrics
                {
                    FilePath = g.Key,
                    DuplicateGroups = g.Select(cb => cb.ElementName).Distinct().Count(),
                    DuplicateLines = g.Sum(cb => cb.EndLine - cb.StartLine + 1),
                    DuplicationPercentage = totalLines > 0 ? (double)g.Sum(cb => cb.EndLine - cb.StartLine + 1) / totalLines * 100 : 0.0,
                    DuplicationTypes = g.Select(cb => DetermineDuplicateType(cb)).Distinct().ToList(),
                    AverageComplexity = g.Average(cb => cb.Complexity.OverallScore)
                });

        // Calculate duplication by similarity ranges
        var similarityRanges = new Dictionary<string, int>
        {
            ["90-100%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.9),
            ["80-89%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.8 && g.SimilarityScore < 0.9),
            ["70-79%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.7 && g.SimilarityScore < 0.8),
            ["60-69%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.6 && g.SimilarityScore < 0.7),
            ["<60%"] = duplicateGroups.Count(g => g.SimilarityScore < 0.6)
        };

        var similarities = duplicateGroups.Select(g => g.SimilarityScore).ToList();
        var avgSimilarity = similarities.Count != 0 ? similarities.Average() : 0.0;
        var maxSimilarity = similarities.Count != 0 ? similarities.Max() : 0.0;
        var minSimilarity = similarities.Count != 0 ? similarities.Min() : 0.0;

        // Estimate costs (simplified calculation)
        var estimatedMaintenanceCost = duplicateLines * 0.5; // 0.5 hours per line of duplicate code
        var estimatedRefactoringSavings = duplicateLines * 0.3; // 0.3 hours saved per line refactored

        return new DuplicationMetrics
        {
            TotalDuplicateGroups = duplicateGroups.Count,
            ExactDuplicateGroups = duplicateGroups.Count(g => g.IsExactDuplicate),
            NearMissDuplicateGroups = duplicateGroups.Count(g => !g.IsExactDuplicate),
            TotalDuplicateLines = duplicateLines,
            DuplicationPercentage = duplicationPercentage,
            FilesWithDuplicates = filesWithDuplicates,
            DuplicationByType = duplicationByType,
            DuplicationByFile = duplicationByFile,
            DuplicationBySimilarity = similarityRanges,
            AverageSimilarity = avgSimilarity,
            MaxSimilarity = maxSimilarity,
            MinSimilarity = minSimilarity,
            EstimatedMaintenanceCost = estimatedMaintenanceCost,
            EstimatedRefactoringSavings = estimatedRefactoringSavings
        };
    }

    private List<RefactoringSuggestion> GenerateRefactoringSuggestions(
        List<DuplicateGroup> duplicateGroups,
        RefactoringOptions options,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RefactoringSuggestion>();

        foreach (var group in duplicateGroups.Take(options.MaxSuggestions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupSuggestions = GenerateSuggestionsForDuplicateGroup(group, options);
            suggestions.AddRange(groupSuggestions);
        }

        return suggestions.OrderByDescending(s => s.Priority)
                         .ThenByDescending(s => s.EstimatedBenefit)
                         .ToList();
    }

    private List<RefactoringSuggestion> GenerateSuggestionsForDuplicateGroup(
        DuplicateGroup group,
        RefactoringOptions options)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var wantsExtractMethod = options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractMethod);
        var wantsUtilityClass = options.RefactoringTypes.HasFlag(RefactoringTypes.UtilityClass);

        // Data-flow based dependency validation before suggesting any "extract shared method" refactoring
        var blockAnalyses = wantsExtractMethod || wantsUtilityClass ? AnalyzeGroupBlocks(group) : null;
        var safety = blockAnalyses != null ? ExtractionDependencyAnalyzer.AnalyzeGroup(blockAnalyses) : null;

        if (wantsExtractMethod && group.DuplicationType is DuplicateType.Method or DuplicateType.Constructor or DuplicateType.CodeBlock)
        {
            suggestions.Add(CreateExtractMethodSuggestion(group, safety));
        }

        if (wantsExtractMethod && group.DuplicationType == DuplicateType.Method)
        {
            suggestions.Add(CreateParameterizeMethodSuggestion(group, safety));
        }

        if (group.DuplicationType == DuplicateType.Class)
        {
            if (options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractBaseClass))
            {
                suggestions.Add(CreateExtractBaseClassSuggestion(group));
            }

            if (options.RefactoringTypes.HasFlag(RefactoringTypes.Composition))
            {
                suggestions.Add(CreateCompositionSuggestion(group));
            }
        }

        if (wantsUtilityClass)
        {
            var staticSafety = blockAnalyses != null ? ExtractionDependencyAnalyzer.AnalyzeGroup(blockAnalyses, requireStaticTarget: true) : null;
            suggestions.Add(CreateUtilityClassSuggestion(group, staticSafety));
        }

        return suggestions;
    }

    private static RefactoringRisk AdjustRiskForSafety(RefactoringRisk risk, ExtractionSafetyResult? safety) =>
        safety is { IsSafe: false } && risk < RefactoringRisk.High ? RefactoringRisk.High : risk;

    private static List<string> ExtractionPrerequisites(ExtractionSafetyResult? safety, params string[] defaults)
    {
        if (safety == null)
        {
            return defaults.ToList();
        }

        var prerequisites = new List<string>();
        var signature = safety.Blocks.Select(b => b.SuggestedSignature).FirstOrDefault(s => s != null);
        if (signature != null)
        {
            prerequisites.Add($"Shared method signature: {signature}");
        }

        prerequisites.AddRange(safety.UnsafeReasons.Select(r => $"Resolve before extracting: {r}"));
        prerequisites.AddRange(safety.Warnings);
        return prerequisites.Count > 0 ? prerequisites : defaults.ToList();
    }

    private static RefactoringSuggestion CreateExtractMethodSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ExtractMethod,
            Title = "Extract Common Method",
            Description = $"Extract the duplicate code into a shared method to eliminate {group.CodeBlocks.Count} instances of duplication.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.2, // 0.2 hours per line
            EstimatedBenefit = group.LineCount * 0.1, // 0.1 hours benefit per line
            Risk = AdjustRiskForSafety(CalculateRisk(group), safety),
            IsBreakingChange = false,
            ImplementationSteps = CreateExtractMethodSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Identify common parameters", "Determine appropriate method name"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "May introduce method parameter complexity", "Requires updating all call sites" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ExtractMethodPattern",
                Confidence = 0.85,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ComplexityReduction"] = group.Complexity * 0.5,
                    ["DuplicationElimination"] = group.CodeBlocks.Count - 1
                }
            }
        };
    }

    private static RefactoringSuggestion CreateParameterizeMethodSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ParameterizeMethod,
            Title = "Parameterize Similar Methods",
            Description = $"Combine {group.CodeBlocks.Count} similar methods into a single parameterized method.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.3,
            EstimatedBenefit = group.LineCount * 0.15,
            Risk = AdjustRiskForSafety(RefactoringRisk.Medium, safety),
            IsBreakingChange = true,
            ImplementationSteps = CreateParameterizeMethodSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Analyze differences between methods", "Identify parameterization points"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "Changes method signatures", "Requires updating all call sites" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ParameterizeMethodPattern",
                Confidence = 0.75,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ParameterComplexity"] = EstimateParameterComplexity(group),
                    ["SignatureChanges"] = group.CodeBlocks.Count
                }
            }
        };
    }

    private static RefactoringSuggestion CreateExtractBaseClassSuggestion(DuplicateGroup group)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ExtractBaseClass,
            Title = "Extract Base Class",
            Description = $"Extract common functionality from {group.CodeBlocks.Count} classes into a shared base class.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.4,
            EstimatedBenefit = group.LineCount * 0.2,
            Risk = RefactoringRisk.High,
            IsBreakingChange = true,
            ImplementationSteps = CreateExtractBaseClassSteps(group),
            Prerequisites = new List<string> { "Verify inheritance hierarchy", "Check for existing base classes" },
            SideEffects = new List<string> { "Changes inheritance structure", "May affect polymorphic behavior" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ExtractBaseClassPattern",
                Confidence = 0.70,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["InheritanceDepth"] = EstimatedInheritanceDepth,
                    ["AffectedClasses"] = group.CodeBlocks.Count
                }
            }
        };
    }

    private static RefactoringSuggestion CreateCompositionSuggestion(DuplicateGroup group)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.Composition,
            Title = "Use Composition Instead of Inheritance",
            Description = $"Replace duplicate functionality with a composable component shared by {group.CodeBlocks.Count} classes.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.35,
            EstimatedBenefit = group.LineCount * 0.18,
            Risk = RefactoringRisk.Medium,
            IsBreakingChange = true,
            ImplementationSteps = CreateCompositionSteps(group),
            Prerequisites = new List<string> { "Design component interface", "Identify injection points" },
            SideEffects = new List<string> { "Requires dependency injection setup", "Changes object composition" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "CompositionPattern",
                Confidence = 0.80,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ComponentComplexity"] = group.Complexity,
                    ["DependencyInjection"] = true
                }
            }
        };
    }

    private static RefactoringSuggestion CreateUtilityClassSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.UtilityClass,
            Title = "Extract Utility Class",
            Description = $"Create a utility class to contain common functionality used by {group.CodeBlocks.Count} locations.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.25,
            EstimatedBenefit = group.LineCount * 0.12,
            Risk = AdjustRiskForSafety(RefactoringRisk.Low, safety),
            IsBreakingChange = false,
            ImplementationSteps = CreateUtilityClassSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Determine utility class name and namespace"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "Introduces new class", "May affect encapsulation" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "UtilityClassPattern",
                Confidence = 0.90,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["MethodCount"] = group.CodeBlocks.Count,
                    ["StaticMembers"] = true
                }
            }
        };
    }

    private static RefactoringPriority CalculatePriority(DuplicateGroup group)
    {
        var priorityScore = 0;

        // Higher priority for more instances
        priorityScore += Math.Min(group.CodeBlocks.Count * 10, 50);

        // Higher priority for larger blocks
        priorityScore += Math.Min(group.LineCount * 2, 30);

        // Higher priority for higher complexity
        priorityScore += Math.Min(group.Complexity * 5, 20);

        return priorityScore switch
        {
            >= 80 => RefactoringPriority.Critical,
            >= 60 => RefactoringPriority.High,
            >= 40 => RefactoringPriority.Medium,
            _ => RefactoringPriority.Low
        };
    }

    private static RefactoringRisk CalculateRisk(DuplicateGroup group)
    {
        var riskScore = 0;

        // Higher risk for larger groups
        if (group.CodeBlocks.Count > 5)
            riskScore += 20;

        // Higher risk for public accessibility
        if (group.CodeBlocks.Any(cb => cb.Accessibility == MCPsharp.Models.Accessibility.Public))
            riskScore += 15;

        // Higher risk for high complexity
        if (group.Complexity > 10)
            riskScore += 15;

        // Higher risk for distributed across many files
        var fileCount = group.CodeBlocks.Select(cb => cb.FilePath).Distinct().Count();
        if (fileCount > 3)
            riskScore += 10;

        return riskScore switch
        {
            >= 40 => RefactoringRisk.VeryHigh,
            >= 30 => RefactoringRisk.High,
            >= 20 => RefactoringRisk.Medium,
            _ => RefactoringRisk.Low
        };
    }

    private static List<RefactoringStep> CreateExtractMethodSteps(DuplicateGroup group)
    {
        var steps = new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create new shared method with appropriate signature",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate class/file" },
                EstimatedMinutes = 15,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common logic to new method",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Update all call sites to use new method",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 5,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };

        return steps;
    }

    private static List<RefactoringStep> CreateParameterizeMethodSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Analyze differences between similar methods",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = 20,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Design parameterized method signature",
                OperationType = RefactoringOperationType.ChangeSignature,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = 15,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Implement parameterized method",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 3,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            },
            new RefactoringStep
            {
                Description = "Replace old methods with calls to parameterized method",
                OperationType = RefactoringOperationType.InlineCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 8,
                IsOptional = false,
                Dependencies = new List<int> { 2 }
            }
        };
    }

    private static List<RefactoringStep> CreateExtractBaseClassSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create base class with common functionality",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 30,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common methods to base class",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Update derived classes to inherit from base class",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 10,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }

    private static List<RefactoringStep> CreateCompositionSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create component interface and implementation",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 25,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common functionality to component",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Add component injection to consuming classes",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 8,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }

    private static List<RefactoringStep> CreateUtilityClassSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create utility class with static methods",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 20,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common logic to utility methods",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Replace duplicate code with utility method calls",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 5,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }

    private static DuplicateHotspotsResult AnalyzeHotspots(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks)
    {
        // File hotspots
        var fileDuplicationScores = new Dictionary<string, double>();
        var fileDuplicateCounts = new Dictionary<string, int>();
        var fileLineCounts = new Dictionary<string, int>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                if (!fileDuplicationScores.ContainsKey(block.FilePath))
                {
                    fileDuplicationScores[block.FilePath] = 0;
                    fileDuplicateCounts[block.FilePath] = 0;
                    fileLineCounts[block.FilePath] = 0;
                }

                fileDuplicationScores[block.FilePath] += group.SimilarityScore * group.Complexity;
                fileDuplicateCounts[block.FilePath]++;
                fileLineCounts[block.FilePath] += block.EndLine - block.StartLine + 1;
            }
        }

        var fileHotspots = fileDuplicationScores
            .Select(kvp => new FileHotspot
            {
                FilePath = kvp.Key,
                DuplicationScore = kvp.Value,
                DuplicateGroupCount = fileDuplicateCounts[kvp.Key],
                DuplicationPercentage = allCodeBlocks.Any(cb => cb.FilePath == kvp.Key)
                    ? (double)fileLineCounts[kvp.Key] / allCodeBlocks.Count(cb => cb.FilePath == kvp.Key) * 100
                    : 0.0,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value, fileDuplicateCounts[kvp.Key])
            })
            .OrderByDescending(f => f.DuplicationScore)
            .Take(10)
            .ToList();

        // Class hotspots
        var classDuplicationScores = new Dictionary<string, (double Score, int Count, string FilePath)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks.Where(b => !string.IsNullOrEmpty(b.ContainingType)))
            {
                var key = $"{block.ContainingType}|{block.FilePath}";
                if (!classDuplicationScores.TryGetValue(key, out var current))
                {
                    current = (0, 0, block.FilePath);
                    classDuplicationScores[key] = current;
                }

                classDuplicationScores[key] = (
                    current.Score + group.SimilarityScore * group.Complexity,
                    current.Count + 1,
                    block.FilePath
                );
            }
        }

        var classHotspots = classDuplicationScores
            .Select(kvp => new ClassHotspot
            {
                ClassName = kvp.Key.Split('|')[0],
                FilePath = kvp.Value.FilePath,
                DuplicationScore = kvp.Value.Score,
                DuplicateGroupCount = kvp.Value.Count,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Score, kvp.Value.Count)
            })
            .OrderByDescending(c => c.DuplicationScore)
            .Take(10)
            .ToList();

        // Method hotspots
        var methodDuplicationScores = new Dictionary<string, (double Score, int Count, string ClassName, string FilePath)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                var key = $"{block.ElementName}|{block.ContainingType}|{block.FilePath}";
                if (!methodDuplicationScores.TryGetValue(key, out var current))
                {
                    current = (0, 0, block.ContainingType ?? "", block.FilePath);
                    methodDuplicationScores[key] = current;
                }

                methodDuplicationScores[key] = (
                    current.Score + group.SimilarityScore * group.Complexity,
                    current.Count + 1,
                    block.ContainingType ?? "",
                    block.FilePath
                );
            }
        }

        var methodHotspots = methodDuplicationScores
            .Select(kvp => new MethodHotspot
            {
                MethodName = kvp.Key.Split('|')[0],
                ClassName = kvp.Value.ClassName,
                FilePath = kvp.Value.FilePath,
                DuplicationScore = kvp.Value.Score,
                DuplicateGroupCount = kvp.Value.Count,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Score, kvp.Value.Count)
            })
            .OrderByDescending(m => m.DuplicationScore)
            .Take(10)
            .ToList();

        // Directory hotspots
        var directoryDuplicationScores = new Dictionary<string, (int Files, int Lines)>();

        foreach (var group in duplicateGroups)
        {
            foreach (var block in group.CodeBlocks)
            {
                var directory = Path.GetDirectoryName(block.FilePath) ?? "";
                if (!directoryDuplicationScores.TryGetValue(directory, out var current))
                {
                    current = (0, 0);
                    directoryDuplicationScores[directory] = current;
                }

                var lines = block.EndLine - block.StartLine + 1;
                directoryDuplicationScores[directory] = (
                    current.Files + 1,
                    current.Lines + lines
                );
            }
        }

        var directoryHotspots = directoryDuplicationScores
            .Select(kvp => new DirectoryHotspot
            {
                DirectoryPath = kvp.Key,
                DuplicationScore = kvp.Value.Lines * 0.1, // Simplified score
                FilesWithDuplicates = kvp.Value.Files,
                TotalDuplicateLines = kvp.Value.Lines,
                RiskLevel = CalculateHotspotRiskLevel(kvp.Value.Lines * 0.1, kvp.Value.Files)
            })
            .OrderByDescending(d => d.DuplicationScore)
            .Take(10)
            .ToList();

        return new DuplicateHotspotsResult
        {
            FileHotspots = fileHotspots,
            ClassHotspots = classHotspots,
            MethodHotspots = methodHotspots,
            DirectoryHotspots = directoryHotspots
        };
    }

    private static HotspotRiskLevel CalculateHotspotRiskLevel(double score, int count)
    {
        if (score >= 100 || count >= 10)
            return HotspotRiskLevel.Critical;

        if (score >= 50 || count >= 5)
            return HotspotRiskLevel.High;

        if (score >= 20 || count >= 3)
            return HotspotRiskLevel.Medium;

        return HotspotRiskLevel.Low;
    }

    #endregion

    #region Helper Methods

    private static bool ShouldExcludeFile(string filePath, DuplicateDetectionOptions options)
    {
        if (options.ExcludedPaths.Any(pattern => filePath.Contains(pattern)))
            return true;

        // Skip generated files by default
        if (options.IgnoreGeneratedCode && filePath.Contains("Generated"))
            return true;

        // Skip test files if requested
        if (options.IgnoreTestCode && (filePath.Contains("Test") || filePath.Contains(".Tests.")))
            return true;

        return false;
    }

    private static string NormalizeCode(string code, DuplicateDetectionOptions options) =>
        DuplicateDetection.DuplicateDetectionUtils.NormalizeCode(code, options);

    private static string ComputeHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    private static string GetOptionsHash(DuplicateDetectionOptions options)
    {
        var key = $"{options.MinBlockSize}-{options.MaxBlockSize}-{options.DetectionTypes}-{options.IgnoreGeneratedCode}-{options.IgnoreTestCode}-{options.SimilarityThreshold}";
        return ComputeHash(key);
    }

    private static ComplexityMetrics CalculateComplexity(SyntaxNode node)
    {
        // Calculate cyclomatic complexity
        var cyclomaticComplexity = 1; // Base complexity

        foreach (var child in node.DescendantNodes())
        {
            if (child is IfStatementSyntax or
                WhileStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                SwitchStatementSyntax or
                CatchClauseSyntax)
            {
                cyclomaticComplexity++;
            }

            if (child is ConditionalExpressionSyntax)
            {
                cyclomaticComplexity++;
            }
        }

        // Calculate cognitive complexity (simplified)
        var cognitiveComplexity = CalculateCognitiveComplexity(node);

        // Count lines of code
        var linesOfCode = node.GetText().Lines.Count;
        var logicalLinesOfCode = node.DescendantNodes().Count(n => n is StatementSyntax);

        // Extract parameter count if applicable
        var parameterCount = 0;
        if (node is MethodDeclarationSyntax method)
        {
            parameterCount = method.ParameterList?.Parameters.Count ?? 0;
        }

        // Calculate nesting depth
        var nestingDepth = CalculateNestingDepth(node);

        var overallScore = (cyclomaticComplexity * 0.4) +
                          (cognitiveComplexity * 0.3) +
                          (nestingDepth * 0.2) +
                          (parameterCount * 0.1);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = cyclomaticComplexity,
            CognitiveComplexity = cognitiveComplexity,
            LinesOfCode = linesOfCode,
            LogicalLinesOfCode = logicalLinesOfCode,
            ParameterCount = parameterCount,
            NestingDepth = nestingDepth,
            OverallScore = overallScore
        };
    }

    private static int CalculateCognitiveComplexity(SyntaxNode node)
    {
        var complexity = 0;

        void VisitNode(SyntaxNode n, int currentNesting)
        {
            switch (n)
            {
                case IfStatementSyntax ifStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(ifStmt.Condition, currentNesting);
                    if (ifStmt.Statement != null)
                        VisitNode(ifStmt.Statement, currentNesting + 1);
                    if (ifStmt.Else?.Statement != null)
                        VisitNode(ifStmt.Else.Statement, currentNesting);
                    break;

                case WhileStatementSyntax whileStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(whileStmt.Condition, currentNesting);
                    VisitNode(whileStmt.Statement, currentNesting + 1);
                    break;

                case ForStatementSyntax forStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(forStmt.Statement, currentNesting + 1);
                    break;

                case ForEachStatementSyntax forEachStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(forEachStmt.Statement, currentNesting + 1);
                    break;

                case SwitchStatementSyntax switchStmt:
                    complexity += 1 + currentNesting;
                    foreach (var section in switchStmt.Sections)
                    {
                        VisitNode(section, currentNesting + 1);
                    }
                    break;

                case CatchClauseSyntax catchClause:
                    complexity += 1;
                    VisitNode(catchClause.Block, currentNesting);
                    break;

                default:
                    foreach (var child in n.ChildNodes())
                    {
                        VisitNode(child, currentNesting);
                    }
                    break;
            }
        }

        VisitNode(node, 0);
        return complexity;
    }

    private static int CalculateNestingDepth(SyntaxNode node)
    {
        var maxDepth = 0;

        void VisitNode(SyntaxNode n, int currentDepth)
        {
            maxDepth = Math.Max(maxDepth, currentDepth);

            switch (n)
            {
                case IfStatementSyntax ifStmt:
                    if (ifStmt.Statement != null)
                        VisitNode(ifStmt.Statement, currentDepth + 1);
                    if (ifStmt.Else?.Statement != null)
                        VisitNode(ifStmt.Else.Statement, currentDepth + 1);
                    break;

                case WhileStatementSyntax whileStmt:
                    VisitNode(whileStmt.Statement, currentDepth + 1);
                    break;

                case ForStatementSyntax forStmt:
                    VisitNode(forStmt.Statement, currentDepth + 1);
                    break;

                case ForEachStatementSyntax forEachStmt:
                    VisitNode(forEachStmt.Statement, currentDepth + 1);
                    break;

                case SwitchStatementSyntax switchStmt:
                    foreach (var section in switchStmt.Sections)
                    {
                        VisitNode(section, currentDepth + 1);
                    }
                    break;

                case TryStatementSyntax tryStmt:
                    VisitNode(tryStmt.Block, currentDepth + 1);
                    foreach (var catchClause in tryStmt.Catches)
                    {
                        VisitNode(catchClause, currentDepth + 1);
                    }
                    if (tryStmt.Finally != null)
                        VisitNode(tryStmt.Finally, currentDepth + 1);
                    break;

                default:
                    foreach (var child in n.ChildNodes())
                    {
                        VisitNode(child, currentDepth);
                    }
                    break;
            }
        }

        VisitNode(node, 0);
        return maxDepth;
    }

    private static List<CodeToken> ExtractTokens(SyntaxNode node)
    {
        var tokens = new List<CodeToken>();

        foreach (var token in node.DescendantTokens())
        {
            if (token.SyntaxTree == null) continue;
            var lineSpan = token.SyntaxTree.GetLineSpan(token.Span);

            tokens.Add(new CodeToken
            {
                Type = GetTokenType(token),
                Text = token.Text,
                StartPosition = token.SpanStart,
                Length = token.Span.Length,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1
            });
        }

        return tokens;
    }

    private static TokenType GetTokenType(SyntaxToken token)
    {
        if (token.IsKeyword())
            return TokenType.Keyword;

        if (token.ValueText != null && (token.Value is string or char))
            return TokenType.StringLiteral;

        if (token.Value != null && IsNumericType(token.Value.GetType()))
            return TokenType.NumericLiteral;

        if (token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
            return TokenType.Identifier;

        // Add more sophisticated token type detection as needed
        return TokenType.Unknown;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    private static AstStructure ExtractAstStructure(SyntaxNode node)
    {
        var nodeTypes = new List<string>();
        var controlFlowPatterns = new List<ControlFlowPattern>();
        var dataFlowPatterns = new List<DataFlowPattern>();

        void CollectNodeTypes(SyntaxNode n)
        {
            nodeTypes.Add(n.GetType().Name);

            switch (n)
            {
                case IfStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Conditional);
                    break;
                case WhileStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case DoStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Loop);
                    break;
                case SwitchStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Switch);
                    break;
                case TryStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.TryCatch);
                    break;
                case ReturnStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Return);
                    break;
                case BreakStatementSyntax:
                case ContinueStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.BreakContinue);
                    break;
            }

            foreach (var child in n.ChildNodes())
            {
                CollectNodeTypes(child);
            }
        }

        CollectNodeTypes(node);

        // Calculate structural hash
        var structuralString = string.Join("|", nodeTypes);
        var structuralHash = ComputeHash(structuralString);

        // Calculate depth and complexity
        var depth = CalculateMaxDepth(node);
        var complexity = Math.Sqrt(nodeTypes.Count) * (1 + depth * 0.1);

        return new AstStructure
        {
            StructuralHash = structuralHash,
            NodeTypes = nodeTypes,
            Depth = depth,
            NodeCount = nodeTypes.Count,
            StructuralComplexity = complexity,
            ControlFlowPatterns = controlFlowPatterns,
            DataFlowPatterns = dataFlowPatterns
        };
    }

    private static int CalculateMaxDepth(SyntaxNode node)
    {
        var maxDepth = 0;

        void VisitNode(SyntaxNode n, int currentDepth)
        {
            maxDepth = Math.Max(maxDepth, currentDepth);
            foreach (var child in n.ChildNodes())
            {
                VisitNode(child, currentDepth + 1);
            }
        }

        VisitNode(node, 0);
        return maxDepth;
    }

    private static bool IsGeneratedCode(SyntaxNode node)
    {
        // Check for generated code attributes
        var attributes = node.DescendantNodesAndSelf().OfType<AttributeSyntax>();
        foreach (var attribute in attributes)
        {
            var attributeName = attribute.Name.ToString();
            if (attributeName.Contains("Generated") ||
                attributeName.Contains("CompilerGenerated") ||
                attributeName.Contains("GeneratedCode"))
            {
                return true;
            }
        }

        // Check for common generated file patterns in file path
        var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
        var filePath = lineSpan.Path ?? "";

        return filePath.Contains("Generated") ||
               filePath.Contains("TemporaryGeneratedFile") ||
               filePath.Contains(".g.cs") ||
               filePath.Contains(".designer.cs");
    }

    private static bool IsTestCode(string filePath)
    {
        return filePath.Contains("Test") ||
               filePath.Contains(".Tests.") ||
               filePath.Contains("Spec") ||
               filePath.Contains("UnitTest");
    }

    private static string GetContainingType(SyntaxNode node)
    {
        var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        return classDecl?.Identifier.Text ?? "";
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var namespaceDecl = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
        return namespaceDecl?.Name.ToString() ?? "";
    }

    private static MCPsharp.Models.Accessibility GetAccessibility(SyntaxNode node)
    {
        var modifiers = node.ChildTokens().FirstOrDefault(t => t.IsKind(SyntaxKind.PublicKeyword) ||
                                                               t.IsKind(SyntaxKind.PrivateKeyword) ||
                                                               t.IsKind(SyntaxKind.InternalKeyword) ||
                                                               t.IsKind(SyntaxKind.ProtectedKeyword));

        if (modifiers.IsKind(SyntaxKind.PublicKeyword))
            return MCPsharp.Models.Accessibility.Public;
        if (modifiers.IsKind(SyntaxKind.PrivateKeyword))
            return MCPsharp.Models.Accessibility.Private;
        if (modifiers.IsKind(SyntaxKind.InternalKeyword))
            return MCPsharp.Models.Accessibility.Internal;
        if (modifiers.IsKind(SyntaxKind.ProtectedKeyword))
            return MCPsharp.Models.Accessibility.Protected;

        return MCPsharp.Models.Accessibility.Private; // Default
    }

    private static CodeContext? ExtractContext(SyntaxNode node, int contextLines)
    {
        if (contextLines <= 0)
            return null;

        var syntaxTree = node.SyntaxTree;
        var lineSpan = syntaxTree.GetLineSpan(node.Span);
        var startLine = lineSpan.StartLinePosition.Line;
        var endLine = lineSpan.EndLinePosition.Line;

        var fullText = syntaxTree.GetText();
        var lines = fullText.Lines;

        var beforeLines = new List<string>();
        var afterLines = new List<string>();

        // Get context lines before
        for (int i = Math.Max(0, startLine - contextLines); i < startLine; i++)
        {
            beforeLines.Add(lines[i].ToString());
        }

        // Get context lines after
        for (int i = endLine + 1; i < Math.Min(lines.Count - 1, endLine + contextLines + 1); i++)
        {
            afterLines.Add(lines[i].ToString());
        }

        return new CodeContext
        {
            BeforeLines = beforeLines,
            AfterLines = afterLines,
            BeforeLineCount = beforeLines.Count,
            AfterLineCount = afterLines.Count
        };
    }

    private static DuplicateType DetermineDuplicateType(CodeBlock codeBlock)
    {
        return codeBlock.ElementType switch
        {
            CodeElementType.Method => DuplicateType.Method,
            CodeElementType.Class => DuplicateType.Class,
            CodeElementType.Property => DuplicateType.Property,
            CodeElementType.Constructor => DuplicateType.Constructor,
            CodeElementType.CodeBlock => DuplicateType.CodeBlock,
            _ => DuplicateType.Unknown
        };
    }

    private static DuplicationImpact CalculateDuplicationImpact(List<CodeBlock> blocks, double similarity)
    {
        var avgComplexity = blocks.Average(cb => cb.Complexity.OverallScore);
        var blockCount = blocks.Count;
        var avgLines = blocks.Average(cb => cb.EndLine - cb.StartLine + 1);

        // Calculate impact scores
        var maintenanceImpact = Math.Min(1.0, (blockCount * avgComplexity * similarity) / 50);
        var readabilityImpact = Math.Min(1.0, (blockCount * 0.3 + avgLines * 0.01) * similarity);
        var performanceImpact = Math.Min(1.0, avgComplexity * 0.05 * similarity);
        var testabilityImpact = Math.Min(1.0, blockCount * 0.2 * similarity);

        var overallImpact = (maintenanceImpact + readabilityImpact + performanceImpact + testabilityImpact) / 4;

        var impactLevel = overallImpact switch
        {
            >= 0.8 => ImpactLevel.Critical,
            >= 0.6 => ImpactLevel.Significant,
            >= 0.4 => ImpactLevel.Moderate,
            >= 0.2 => ImpactLevel.Minor,
            _ => ImpactLevel.Negligible
        };

        return new DuplicationImpact
        {
            MaintenanceImpact = maintenanceImpact,
            ReadabilityImpact = readabilityImpact,
            PerformanceImpact = performanceImpact,
            TestabilityImpact = testabilityImpact,
            OverallImpact = overallImpact,
            ImpactLevel = impactLevel
        };
    }

    private static double EstimateParameterComplexity(DuplicateGroup group)
    {
        // Simplified estimation based on line count and complexity
        return group.LineCount * 0.1 + group.Complexity * 0.2;
    }

    // Simplified estimation - assume moderate inheritance depth
    private const int EstimatedInheritanceDepth = 2;

    #endregion

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting duplicate code detection for project: {ProjectPath}")]
    private static partial void LogStartingDuplicateDetection(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate detection completed in {Duration}ms. Found {GroupCount} duplicate groups affecting {FileCount} files.")]
    private static partial void LogDuplicateDetectionCompleted(ILogger logger, double duration, int groupCount, int fileCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during duplicate code detection")]
    private static partial void LogDuplicateDetectionError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting duplicate detection for {FileCount} specific files")]
    private static partial void LogStartingFileDuplicateDetection(ILogger logger, int fileCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during duplicate detection in specific files")]
    private static partial void LogFileDuplicateDetectionError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialize Roslyn workspace for path: {ProjectPath}")]
    private static partial void LogWorkspaceInitializationFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No compilation available for workspace")]
    private static partial void LogNoCompilation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract code blocks from file: {FilePath}")]
    private static partial void LogCodeBlockExtractionFailed(ILogger logger, Exception exception, string filePath);
}