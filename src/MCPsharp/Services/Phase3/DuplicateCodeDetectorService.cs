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

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Service for detecting duplicate code blocks and providing refactoring suggestions
/// </summary>
public class DuplicateCodeDetectorService : IDuplicateCodeDetectorService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<DuplicateCodeDetectorService> _logger;

    // Cache for compiled results
    private readonly ConcurrentDictionary<string, Compilation> _compilationCache = new();
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

        _logger.LogInformation("Starting duplicate code detection for project: {ProjectPath}", projectPath);

        try
        {
            // Initialize workspace if needed
            await EnsureWorkspaceInitialized(projectPath, cancellationToken);

            // Extract code blocks from all files
            var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);

            // Find duplicates using different strategies
            var exactDuplicates = await FindExactDuplicatesInternalAsync(allCodeBlocks, options, cancellationToken);
            var nearDuplicates = await FindNearDuplicatesInternalAsync(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

            // Combine and group duplicates
            var allDuplicateGroups = new List<DuplicateGroup>();
            allDuplicateGroups.AddRange(exactDuplicates);
            allDuplicateGroups.AddRange(nearDuplicates);

            // Calculate metrics
            var metrics = await CalculateDuplicationMetricsAsync(allDuplicateGroups, allCodeBlocks, cancellationToken);

            // Generate refactoring suggestions
            var refactoringOptions = new RefactoringOptions
            {
                MaxSuggestions = 20
            };
            var refactoringSuggestions = await GenerateRefactoringSuggestionsAsync(allDuplicateGroups, refactoringOptions, cancellationToken);

            // Get hotspots
            var hotspots = await AnalyzeHotspotsAsync(allDuplicateGroups, allCodeBlocks, cancellationToken);

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Duplicate detection completed in {Duration}ms. Found {GroupCount} duplicate groups affecting {FileCount} files.",
                duration.TotalMilliseconds, allDuplicateGroups.Count, metrics.FilesWithDuplicates);

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
            _logger.LogError(ex, "Error during duplicate code detection");
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
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        return await FindExactDuplicatesInternalAsync(allCodeBlocks, options, cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
        string projectPath,
        double similarityThreshold = 0.8,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        if (options.SimilarityThreshold != similarityThreshold)
        {
            options = options with { SimilarityThreshold = similarityThreshold };
        }
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        return await FindNearDuplicatesInternalAsync(allCodeBlocks, similarityThreshold, options, cancellationToken);
    }

    public async Task<DuplicationMetrics> AnalyzeDuplicationMetricsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var exactDuplicates = await FindExactDuplicatesInternalAsync(allCodeBlocks, options, cancellationToken);
        var nearDuplicates = await FindNearDuplicatesInternalAsync(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

        var allGroups = new List<DuplicateGroup>();
        allGroups.AddRange(exactDuplicates);
        allGroups.AddRange(nearDuplicates);

        return await CalculateDuplicationMetricsAsync(allGroups, allCodeBlocks, cancellationToken);
    }

    public async Task<IReadOnlyList<RefactoringSuggestion>> GetRefactoringSuggestionsAsync(
        string projectPath,
        IReadOnlyList<DuplicateGroup> duplicates,
        RefactoringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RefactoringOptions();
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        return await GenerateRefactoringSuggestionsAsync(duplicates.ToList(), options, cancellationToken);
    }

    public async Task<DuplicateDetectionResult> DetectDuplicatesInFilesAsync(
        IReadOnlyList<string> filePaths,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new DuplicateDetectionOptions();

        _logger.LogInformation("Starting duplicate detection for {FileCount} specific files", filePaths.Count);

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
            var exactDuplicates = await FindExactDuplicatesInternalAsync(allCodeBlocks, options, cancellationToken);
            var nearDuplicates = await FindNearDuplicatesInternalAsync(allCodeBlocks, options.SimilarityThreshold, options, cancellationToken);

            var allDuplicateGroups = new List<DuplicateGroup>();
            allDuplicateGroups.AddRange(exactDuplicates);
            allDuplicateGroups.AddRange(nearDuplicates);

            var metrics = await CalculateDuplicationMetricsAsync(allDuplicateGroups, allCodeBlocks, cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate detection in specific files");
            throw;
        }
    }

    public async Task<CodeSimilarityResult> CompareCodeBlocksAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CodeComparisonOptions();

        // Calculate different similarity scores
        var structuralSimilarity = await CalculateStructuralSimilarityAsync(codeBlock1, codeBlock2, cancellationToken);
        var tokenSimilarity = await CalculateTokenSimilarityAsync(codeBlock1, codeBlock2, options, cancellationToken);
        var semanticSimilarity = await CalculateSemanticSimilarityAsync(codeBlock1, codeBlock2, cancellationToken);

        // Calculate overall similarity
        var overallSimilarity = (structuralSimilarity * options.StructuralWeight) +
                                (tokenSimilarity * options.TokenWeight);

        // Find differences
        var differences = await FindCodeDifferencesAsync(codeBlock1, codeBlock2, options, cancellationToken);

        // Find common patterns
        var commonPatterns = await FindCommonPatternsAsync(codeBlock1, codeBlock2, cancellationToken);

        // Determine if they are duplicates and what type
        var isDuplicate = overallSimilarity >= 0.8; // Default threshold
        var duplicateType = await DetermineDuplicateTypeAsync(codeBlock1, codeBlock2, overallSimilarity, cancellationToken);

        return new CodeSimilarityResult
        {
            OverallSimilarity = overallSimilarity,
            StructuralSimilarity = structuralSimilarity,
            TokenSimilarity = tokenSimilarity,
            SemanticSimilarity = semanticSimilarity,
            Differences = differences,
            CommonPatterns = commonPatterns,
            IsDuplicate = isDuplicate,
            DuplicateType = duplicateType
        };
    }

    public async Task<DuplicateHotspotsResult> GetDuplicateHotspotsAsync(
        string projectPath,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        var allCodeBlocks = await ExtractAllCodeBlocksAsync(projectPath, options, cancellationToken);
        var duplicates = await DetectDuplicatesAsync(projectPath, options, cancellationToken);

        return await AnalyzeHotspotsAsync(duplicates.DuplicateGroups.ToList(), allCodeBlocks, cancellationToken);
    }

    public async Task<RefactoringValidationResult> ValidateRefactoringAsync(
        string projectPath,
        RefactoringSuggestion suggestion,
        CancellationToken cancellationToken = default)
    {
        await EnsureWorkspaceInitialized(projectPath, cancellationToken);

        var issues = new List<ValidationIssue>();
        var dependencyImpacts = new List<DependencyImpact>();
        var riskLevel = RefactoringRisk.Low;

        // Validate that the refactoring won't break dependencies
        foreach (var groupId in suggestion.DuplicateGroupIds)
        {
            // Basic dependency validation - future enhancement for detailed analysis
            // TODO: Implement comprehensive dependency validation logic using Roslyn call analysis
            // This would involve checking if any external code depends on the duplicated code
            // For now, we perform a basic check
            await ValidateBasicDependenciesAsync(groupId, dependencyImpacts, cancellationToken);
        }

        // Check for breaking changes
        if (suggestion.IsBreakingChange)
        {
            riskLevel = RefactoringRisk.High;
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
            IsValid = issues.All(i => i.Severity != ValidationSeverity.Critical),
            Issues = issues,
            DependencyImpacts = dependencyImpacts,
            OverallRisk = riskLevel,
            Recommendations = recommendations
        };
    }

    #region Private Implementation Methods

    private async Task EnsureWorkspaceInitialized(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            await _workspace.InitializeAsync(projectPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Roslyn workspace for path: {ProjectPath}", projectPath);
            throw;
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
        var compilation = _workspace.GetCompilation();

        if (compilation == null)
        {
            _logger.LogWarning("No compilation available for workspace");
            return allCodeBlocks;
        }

        // Process each syntax tree
        foreach (var syntaxTree in compilation.SyntaxTrees)
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
                _logger.LogWarning(ex, "Failed to extract code blocks from file: {FilePath}", filePath);
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
        var codeBlocks = new List<CodeBlock>();
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var semanticModel = _workspace.GetCompilation()?.GetSemanticModel(syntaxTree);

        if (root == null || semanticModel == null)
            return codeBlocks;

        // Extract different types of code blocks based on options
        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Methods))
        {
            var methodBlocks = ExtractMethodBlocks(root, semanticModel, syntaxTree.FilePath, options);
            codeBlocks.AddRange(methodBlocks);
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Classes))
        {
            var classBlocks = ExtractClassBlocks(root, semanticModel, syntaxTree.FilePath, options);
            codeBlocks.AddRange(classBlocks);
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.CodeBlocks))
        {
            var blockBlocks = ExtractCodeBlockStatements(root, semanticModel, syntaxTree.FilePath, options);
            codeBlocks.AddRange(blockBlocks);
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Properties))
        {
            var propertyBlocks = ExtractPropertyBlocks(root, semanticModel, syntaxTree.FilePath, options);
            codeBlocks.AddRange(propertyBlocks);
        }

        if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Constructors))
        {
            var constructorBlocks = ExtractConstructorBlocks(root, semanticModel, syntaxTree.FilePath, options);
            codeBlocks.AddRange(constructorBlocks);
        }

        return codeBlocks;
    }

    private List<CodeBlock> ExtractMethodBlocks(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        DuplicateDetectionOptions options)
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
            var isGenerated = IsGeneratedCode(method, semanticModel);
            var isTestCode = IsTestCode(method, filePath);

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
                Context = ExtractContext(method, options.ContextLines)
            });
        }

        return methodBlocks;
    }

    private List<CodeBlock> ExtractClassBlocks(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        DuplicateDetectionOptions options)
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
            var isGenerated = IsGeneratedCode(classDecl, semanticModel);
            var isTestCode = IsTestCode(classDecl, filePath);

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
                Context = ExtractContext(classDecl, options.ContextLines)
            });
        }

        return classBlocks;
    }

    private List<CodeBlock> ExtractCodeBlockStatements(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        DuplicateDetectionOptions options)
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
            var isGenerated = containingMethod != null && IsGeneratedCode(containingMethod, semanticModel);
            var isTestCode = IsTestCode(block, filePath);

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
                Context = ExtractContext(block, options.ContextLines)
            });
        }

        return blockBlocks;
    }

    private List<CodeBlock> ExtractPropertyBlocks(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        DuplicateDetectionOptions options)
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
            var isGenerated = IsGeneratedCode(property, semanticModel);
            var isTestCode = IsTestCode(property, filePath);

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
                Context = ExtractContext(property, options.ContextLines)
            });
        }

        return propertyBlocks;
    }

    private List<CodeBlock> ExtractConstructorBlocks(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        DuplicateDetectionOptions options)
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
            var isGenerated = IsGeneratedCode(constructor, semanticModel);
            var isTestCode = IsTestCode(constructor, filePath);

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
                Context = ExtractContext(constructor, options.ContextLines)
            });
        }

        return constructorBlocks;
    }

    private async Task<List<CodeBlock>> ExtractCodeBlocksFromFileAsync(
        string filePath,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var codeBlocks = new List<CodeBlock>();

        try
        {
            var sourceText = await File.ReadAllTextAsync(filePath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var compilation = _workspace.GetCompilation();
            var semanticModel = compilation?.GetSemanticModel(syntaxTree);

            if (semanticModel == null)
                return codeBlocks;

            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Methods))
            {
                codeBlocks.AddRange(ExtractMethodBlocks(root, semanticModel, filePath, options));
            }

            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Classes))
            {
                codeBlocks.AddRange(ExtractClassBlocks(root, semanticModel, filePath, options));
            }

            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.CodeBlocks))
            {
                codeBlocks.AddRange(ExtractCodeBlockStatements(root, semanticModel, filePath, options));
            }

            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Properties))
            {
                codeBlocks.AddRange(ExtractPropertyBlocks(root, semanticModel, filePath, options));
            }

            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Constructors))
            {
                codeBlocks.AddRange(ExtractConstructorBlocks(root, semanticModel, filePath, options));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract code blocks from file: {FilePath}", filePath);
        }

        return codeBlocks;
    }

    private async Task<List<DuplicateGroup>> FindExactDuplicatesInternalAsync(
        List<CodeBlock> codeBlocks,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var hashGroups = codeBlocks.GroupBy(cb => cb.CodeHash);

        foreach (var hashGroup in hashGroups)
        {
            if (hashGroup.Count() < 2) continue; // Need at least 2 blocks to be a duplicate

            var blocksList = hashGroup.ToList();
            var similarityScore = 1.0; // Exact duplicates have 100% similarity
            var lineCount = blocksList.First().EndLine - blocksList.First().StartLine + 1;
            var complexity = blocksList.First().Complexity.OverallScore;
            var duplicateType = DetermineDuplicateType(blocksList.First());

            duplicateGroups.Add(new DuplicateGroup
            {
                GroupId = Guid.NewGuid().ToString(),
                CodeBlocks = blocksList,
                SimilarityScore = similarityScore,
                DuplicationType = duplicateType,
                LineCount = lineCount,
                Complexity = (int)complexity,
                IsExactDuplicate = true,
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

    private async Task<List<DuplicateGroup>> FindNearDuplicatesInternalAsync(
        List<CodeBlock> codeBlocks,
        double similarityThreshold,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var processedPairs = new HashSet<string>();

        // Group by element type and similar size for efficiency
        var typeGroups = codeBlocks.GroupBy(cb => cb.ElementType);

        foreach (var typeGroup in typeGroups)
        {
            var blocksBySize = typeGroup.GroupBy(cb => cb.EndLine - cb.StartLine + 1);

            foreach (var sizeGroup in blocksBySize)
            {
                var blocksList = sizeGroup.ToList();

                for (int i = 0; i < blocksList.Count; i++)
                {
                    for (int j = i + 1; j < blocksList.Count; j++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return duplicateGroups;

                        var block1 = blocksList[i];
                        var block2 = blocksList[j];

                        // Skip if already processed this pair
                        var pairKey = $"{Math.Min(block1.CodeHash.GetHashCode(), block2.CodeHash.GetHashCode())}_{Math.Max(block1.CodeHash.GetHashCode(), block2.CodeHash.GetHashCode())}";
                        if (processedPairs.Contains(pairKey))
                            continue;

                        processedPairs.Add(pairKey);

                        // Calculate similarity
                        var similarity = await CalculateSimilarityAsync(block1, block2, options, cancellationToken);

                        if (similarity >= similarityThreshold)
                        {
                            // Find or create duplicate group
                            var existingGroup = FindOrCreateNearDuplicateGroup(duplicateGroups, block1, block2, similarity, options);

                            if (existingGroup == null)
                            {
                                duplicateGroups.Add(CreateNearDuplicateGroup(block1, block2, similarity, options));
                            }
                        }
                    }
                }
            }
        }

        return duplicateGroups.OrderByDescending(g => g.SimilarityScore).ThenByDescending(g => g.Complexity).ToList();
    }

    private async Task<double> CalculateSimilarityAsync(
        CodeBlock block1,
        CodeBlock block2,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        // Quick hash-based comparison first
        if (block1.CodeHash == block2.CodeHash)
            return 1.0;

        // Calculate structural similarity
        var structuralSim = CalculateStructuralSimilarity(block1, block2);

        // Calculate token similarity
        var tokenSim = CalculateTokenSimilarity(block1, block2, options);

        // Weighted combination
        var overallSimilarity = (structuralSim * 0.7) + (tokenSim * 0.3);

        return overallSimilarity;
    }

    private double CalculateStructuralSimilarity(CodeBlock block1, CodeBlock block2)
    {
        // Compare AST structure hashes
        if (block1.AstStructure.StructuralHash == block2.AstStructure.StructuralHash)
            return 1.0;

        // Compare node types sequences
        var seq1 = block1.AstStructure.NodeTypes;
        var seq2 = block2.AstStructure.NodeTypes;

        if (seq1.Count != seq2.Count)
            return 0.0;

        var matches = 0;
        for (int i = 0; i < seq1.Count; i++)
        {
            if (seq1[i] == seq2[i])
                matches++;
        }

        return (double)matches / seq1.Count;
    }

    private double CalculateTokenSimilarity(CodeBlock block1, CodeBlock block2, DuplicateDetectionOptions options)
    {
        if (block1.Tokens == null || block2.Tokens == null)
            return 0.0;

        var tokens1 = FilterTokens(block1.Tokens, options);
        var tokens2 = FilterTokens(block2.Tokens, options);

        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 1.0;

        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        // Use Levenshtein distance on token sequences
        var distance = CalculateLevenshteinDistance(
            tokens1.Select(t => t.Type).ToList(),
            tokens2.Select(t => t.Type).ToList());

        var maxLen = Math.Max(tokens1.Count, tokens2.Count);
        return 1.0 - ((double)distance / maxLen);
    }

    private List<CodeToken> FilterTokens(IReadOnlyList<CodeToken> tokens, DuplicateDetectionOptions options)
    {
        var filtered = new List<CodeToken>();

        foreach (var token in tokens)
        {
            if (options.IgnoreTrivialDifferences && token.Type == TokenType.Whitespace)
                continue;

            if (options.IgnoreTrivialDifferences && token.Type == TokenType.Comment)
                continue;

            if (options.IgnoreIdentifiers && token.Type == TokenType.Identifier)
                continue;

            filtered.Add(token);
        }

        return filtered;
    }

    private int CalculateLevenshteinDistance<T>(IList<T> sequence1, IList<T> sequence2)
    {
        var matrix = new int[sequence1.Count + 1, sequence2.Count + 1];

        for (int i = 0; i <= sequence1.Count; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= sequence2.Count; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= sequence1.Count; i++)
        {
            for (int j = 1; j <= sequence2.Count; j++)
            {
                var cost = sequence1[i - 1]!.Equals(sequence2[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[sequence1.Count, sequence2.Count];
    }

    private async Task<double> CalculateStructuralSimilarityAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
    {
        return CalculateStructuralSimilarity(codeBlock1, codeBlock2);
    }

    private async Task<double> CalculateTokenSimilarityAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var detectionOptions = new DuplicateDetectionOptions
        {
            IgnoreTrivialDifferences = options.IgnoreWhitespace && options.IgnoreComments,
            IgnoreIdentifiers = options.IgnoreIdentifiers
        };

        return CalculateTokenSimilarity(codeBlock1, codeBlock2, detectionOptions);
    }

    private async Task<double> CalculateSemanticSimilarityAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
    {
        // TODO: Enhance semantic similarity using Roslyn semantic analysis
        // Future enhancement: Use syntax tree comparison and type analysis
        // For now, return a basic similarity based on element types
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return 0.0;

        if (codeBlock1.ContainingType == codeBlock2.ContainingType)
            return 0.9;

        if (codeBlock1.Namespace == codeBlock2.Namespace)
            return 0.7;

        return 0.5;
    }

    private async Task<List<CodeDifference>> FindCodeDifferencesAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var differences = new List<CodeDifference>();

        // TODO: Enhance detailed difference analysis
        // Future enhancement: Use syntax tree diffing to identify exact changes
        // For now, return basic difference information
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
                Impact = 0.5
            });
        }

        return differences;
    }

    private async Task<List<CommonPattern>> FindCommonPatternsAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
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

    private async Task<DuplicateType?> DetermineDuplicateTypeAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        double similarity,
        CancellationToken cancellationToken)
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

    private DuplicateGroup? FindOrCreateNearDuplicateGroup(
        List<DuplicateGroup> groups,
        CodeBlock block1,
        CodeBlock block2,
        double similarity,
        DuplicateDetectionOptions options)
    {
        // Find if either block is already in a group
        var existingGroup = groups.FirstOrDefault(g =>
            g.CodeBlocks.Any(cb => cb.CodeHash == block1.CodeHash || cb.CodeHash == block2.CodeHash));

        if (existingGroup != null)
        {
            // Check if the other block should be added to this group
            if (!existingGroup.CodeBlocks.Any(cb => cb.CodeHash == block1.CodeHash))
            {
                var updatedBlocks = existingGroup.CodeBlocks.Append(block1).ToList();
                // Update group properties based on all blocks
                existingGroup = UpdateDuplicateGroup(existingGroup, updatedBlocks);
            }
            else if (!existingGroup.CodeBlocks.Any(cb => cb.CodeHash == block2.CodeHash))
            {
                var updatedBlocks = existingGroup.CodeBlocks.Append(block2).ToList();
                existingGroup = UpdateDuplicateGroup(existingGroup, updatedBlocks);
            }
        }

        return existingGroup;
    }

    private DuplicateGroup CreateNearDuplicateGroup(
        CodeBlock block1,
        CodeBlock block2,
        double similarity,
        DuplicateDetectionOptions options)
    {
        var blocks = new List<CodeBlock> { block1, block2 };
        var lineCount = (block1.EndLine - block1.StartLine + 1 + block2.EndLine - block2.StartLine + 1) / 2;
        var complexity = (int)((block1.Complexity.OverallScore + block2.Complexity.OverallScore) / 2);
        var duplicateType = DetermineDuplicateType(block1);

        return new DuplicateGroup
        {
            GroupId = Guid.NewGuid().ToString(),
            CodeBlocks = blocks,
            SimilarityScore = similarity,
            DuplicationType = duplicateType,
            LineCount = lineCount,
            Complexity = complexity,
            IsExactDuplicate = false,
            Impact = CalculateDuplicationImpact(blocks, similarity),
            Metadata = new DuplicateMetadata
            {
                DetectedAt = DateTime.UtcNow,
                DetectionAlgorithm = "NearSimilarityMatch",
                DetectionConfiguration = GetOptionsHash(options),
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["SimilarityThreshold"] = options.SimilarityThreshold,
                    ["ComparisonMethod"] = "AST+Token"
                }
            }
        };
    }

    private DuplicateGroup UpdateDuplicateGroup(DuplicateGroup group, List<CodeBlock> allBlocks)
    {
        // Recalculate group properties based on all blocks
        var avgLineCount = (int)allBlocks.Average(cb => cb.EndLine - cb.StartLine + 1);
        var avgComplexity = (int)allBlocks.Average(cb => cb.Complexity.OverallScore);

        return new DuplicateGroup
        {
            GroupId = group.GroupId,
            CodeBlocks = allBlocks,
            SimilarityScore = group.SimilarityScore,
            DuplicationType = group.DuplicationType,
            LineCount = avgLineCount,
            Complexity = avgComplexity,
            IsExactDuplicate = group.IsExactDuplicate,
            RefactoringSuggestions = group.RefactoringSuggestions,
            Impact = CalculateDuplicationImpact(allBlocks, group.SimilarityScore),
            Metadata = group.Metadata ?? new DuplicateMetadata
            {
                DetectedAt = DateTime.UtcNow,
                DetectionAlgorithm = "StructuralHash",
                DetectionConfiguration = "Default",
                AdditionalMetadata = new Dictionary<string, object>()
            }
        };
    }

    private async Task<DuplicationMetrics> CalculateDuplicationMetricsAsync(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks,
        CancellationToken cancellationToken)
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
        var avgSimilarity = similarities.Any() ? similarities.Average() : 0.0;
        var maxSimilarity = similarities.Any() ? similarities.Max() : 0.0;
        var minSimilarity = similarities.Any() ? similarities.Min() : 0.0;

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

    private async Task<IReadOnlyList<RefactoringSuggestion>> GenerateRefactoringSuggestionsAsync(
        List<DuplicateGroup> duplicateGroups,
        RefactoringOptions options,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RefactoringSuggestion>();

        foreach (var group in duplicateGroups.Take(options.MaxSuggestions))
        {
            var groupSuggestions = await GenerateSuggestionsForDuplicateGroup(group, options, cancellationToken);
            suggestions.AddRange(groupSuggestions);
        }

        return suggestions.OrderByDescending(s => s.Priority)
                         .ThenByDescending(s => s.EstimatedBenefit)
                         .ToList();
    }

    private async Task<List<RefactoringSuggestion>> GenerateSuggestionsForDuplicateGroup(
        DuplicateGroup group,
        RefactoringOptions options,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RefactoringSuggestion>();

        if (group.DuplicationType == DuplicateType.Method)
        {
            if (options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractMethod))
            {
                suggestions.Add(CreateExtractMethodSuggestion(group));
            }

            if (options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractMethod))
            {
                suggestions.Add(CreateParameterizeMethodSuggestion(group));
            }
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

        if (options.RefactoringTypes.HasFlag(RefactoringTypes.UtilityClass))
        {
            suggestions.Add(CreateUtilityClassSuggestion(group));
        }

        return suggestions;
    }

    private RefactoringSuggestion CreateExtractMethodSuggestion(DuplicateGroup group)
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
            Risk = CalculateRisk(group),
            IsBreakingChange = false,
            ImplementationSteps = CreateExtractMethodSteps(group),
            Prerequisites = new List<string> { "Identify common parameters", "Determine appropriate method name" },
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

    private RefactoringSuggestion CreateParameterizeMethodSuggestion(DuplicateGroup group)
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
            Risk = RefactoringRisk.Medium,
            IsBreakingChange = true,
            ImplementationSteps = CreateParameterizeMethodSteps(group),
            Prerequisites = new List<string> { "Analyze differences between methods", "Identify parameterization points" },
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

    private RefactoringSuggestion CreateExtractBaseClassSuggestion(DuplicateGroup group)
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
                    ["InheritanceDepth"] = EstimateInheritanceDepth(group),
                    ["AffectedClasses"] = group.CodeBlocks.Count
                }
            }
        };
    }

    private RefactoringSuggestion CreateCompositionSuggestion(DuplicateGroup group)
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

    private RefactoringSuggestion CreateUtilityClassSuggestion(DuplicateGroup group)
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
            Risk = RefactoringRisk.Low,
            IsBreakingChange = false,
            ImplementationSteps = CreateUtilityClassSteps(group),
            Prerequisites = new List<string> { "Determine utility class name and namespace" },
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

    private RefactoringPriority CalculatePriority(DuplicateGroup group)
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

    private RefactoringRisk CalculateRisk(DuplicateGroup group)
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

    private List<RefactoringStep> CreateExtractMethodSteps(DuplicateGroup group)
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

    private List<RefactoringStep> CreateParameterizeMethodSteps(DuplicateGroup group)
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

    private List<RefactoringStep> CreateExtractBaseClassSteps(DuplicateGroup group)
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

    private List<RefactoringStep> CreateCompositionSteps(DuplicateGroup group)
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

    private List<RefactoringStep> CreateUtilityClassSteps(DuplicateGroup group)
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

    private async Task<DuplicateHotspotsResult> AnalyzeHotspotsAsync(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks,
        CancellationToken cancellationToken)
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
            foreach (var block in group.CodeBlocks)
            {
                if (!string.IsNullOrEmpty(block.ContainingType))
                {
                    var key = $"{block.ContainingType}|{block.FilePath}";
                    if (!classDuplicationScores.ContainsKey(key))
                    {
                        classDuplicationScores[key] = (0, 0, block.FilePath);
                    }

                    var current = classDuplicationScores[key];
                    classDuplicationScores[key] = (
                        current.Score + group.SimilarityScore * group.Complexity,
                        current.Count + 1,
                        block.FilePath
                    );
                }
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
                if (!methodDuplicationScores.ContainsKey(key))
                {
                    methodDuplicationScores[key] = (0, 0, block.ContainingType ?? "", block.FilePath);
                }

                var current = methodDuplicationScores[key];
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
                if (!directoryDuplicationScores.ContainsKey(directory))
                {
                    directoryDuplicationScores[directory] = (0, 0);
                }

                var current = directoryDuplicationScores[directory];
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
            DirectoryHotspots = directoryHotspots,
            Trends = null // TODO: Implement trend analysis if historical data is available
                        // Future enhancement: Track duplicate code patterns over time
        };
    }

    private HotspotRiskLevel CalculateHotspotRiskLevel(double score, int count)
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

    private bool ShouldExcludeFile(string filePath, DuplicateDetectionOptions options)
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

    private string NormalizeCode(string code, DuplicateDetectionOptions options)
    {
        if (options.IgnoreTrivialDifferences)
        {
            // Remove comments
            code = Regex.Replace(code, @"//.*$", "", RegexOptions.Multiline);
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Normalize whitespace
            code = Regex.Replace(code, @"\s+", " ");
            code = code.Trim();
        }

        if (options.IgnoreIdentifiers)
        {
            // Replace identifiers with placeholder
            code = Regex.Replace(code, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b", "IDENTIFIER");
        }

        return code;
    }

    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private string GetOptionsHash(DuplicateDetectionOptions options)
    {
        var key = $"{options.MinBlockSize}-{options.MaxBlockSize}-{options.DetectionTypes}-{options.IgnoreGeneratedCode}-{options.IgnoreTestCode}-{options.SimilarityThreshold}";
        return ComputeHash(key);
    }

    private ComplexityMetrics CalculateComplexity(SyntaxNode node)
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

    private int CalculateCognitiveComplexity(SyntaxNode node)
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

    private int CalculateNestingDepth(SyntaxNode node)
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

    private List<CodeToken> ExtractTokens(SyntaxNode node)
    {
        var tokens = new List<CodeToken>();

        foreach (var token in node.DescendantTokens())
        {
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

    private TokenType GetTokenType(SyntaxToken token)
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

    private bool IsNumericType(Type type)
    {
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    private AstStructure ExtractAstStructure(SyntaxNode node)
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

    private int CalculateMaxDepth(SyntaxNode node)
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

    private bool IsGeneratedCode(SyntaxNode node, SemanticModel semanticModel)
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

    private bool IsTestCode(SyntaxNode node, string filePath)
    {
        return filePath.Contains("Test") ||
               filePath.Contains(".Tests.") ||
               filePath.Contains("Spec") ||
               filePath.Contains("UnitTest");
    }

    private string GetContainingType(SyntaxNode node)
    {
        var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        return classDecl?.Identifier.Text ?? "";
    }

    private string GetNamespace(SyntaxNode node)
    {
        var namespaceDecl = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
        return namespaceDecl?.Name.ToString() ?? "";
    }

    private MCPsharp.Models.Accessibility GetAccessibility(SyntaxNode node)
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

    private CodeContext ExtractContext(SyntaxNode node, int contextLines)
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

    private DuplicateType DetermineDuplicateType(CodeBlock codeBlock)
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

    private DuplicationImpact CalculateDuplicationImpact(List<CodeBlock> blocks, double similarity)
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

    private double EstimateParameterComplexity(DuplicateGroup group)
    {
        // Simplified estimation based on line count and complexity
        return group.LineCount * 0.1 + group.Complexity * 0.2;
    }

    private int EstimateInheritanceDepth(DuplicateGroup group)
    {
        // Simplified estimation - assume moderate inheritance depth
        return 2;
    }

    #endregion

    #region Dependency Validation

    /// <summary>
    /// Basic dependency validation for refactoring suggestions
    /// TODO: Enhance with comprehensive Roslyn-based call analysis
    /// </summary>
    private async Task ValidateBasicDependenciesAsync(
        string groupId,
        List<DependencyImpact> dependencyImpacts,
        CancellationToken cancellationToken)
    {
        // Basic implementation - check if duplicate groups contain public methods
        // that could have external dependencies
        // For now, add a low-risk dependency impact note
        // Future enhancement: Use Roslyn to analyze actual call chains
        dependencyImpacts.Add(new DependencyImpact
        {
            DependentFile = $"DuplicateGroup_{groupId}",
            ImpactType = DependencyImpactType.Minor,
            Description = $"Duplicate code group {groupId} may have external dependencies",
            IsBreakingChange = false
        });

        await Task.CompletedTask;
    }

    #endregion
}