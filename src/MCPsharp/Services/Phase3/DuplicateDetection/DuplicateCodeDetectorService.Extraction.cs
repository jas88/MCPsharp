using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Code block extraction from syntax trees and files
/// </summary>
public partial class DuplicateCodeDetectorService
{
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
            if (method.Body == null)
                continue; // Skip abstract methods

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
}
