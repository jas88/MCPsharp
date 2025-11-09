using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Service for extracting code blocks from C# source files
/// </summary>
public class CodeBlockExtractorService
{
    private readonly ILogger<CodeBlockExtractorService> _logger;

    public CodeBlockExtractorService(ILogger<CodeBlockExtractorService> logger)
    {
        _logger = logger;
    }

    public async Task<List<CodeBlock>> ExtractAllCodeBlocksAsync(
        string projectPath,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var allCodeBlocks = new List<CodeBlock>();

        try
        {
            var csharpFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories);

            foreach (var filePath in csharpFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (ShouldExcludeFile(filePath, options))
                    continue;

                var fileCodeBlocks = await ExtractCodeBlocksFromFileAsync(filePath, options, cancellationToken);
                allCodeBlocks.AddRange(fileCodeBlocks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code blocks from project");
        }

        return allCodeBlocks;
    }

    public async Task<List<CodeBlock>> ExtractCodeBlocksFromSyntaxTreeAsync(
        SyntaxTree syntaxTree,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var codeBlocks = new List<CodeBlock>();

        try
        {
            var root = syntaxTree.GetCompilationUnitSyntax();

            // Extract method blocks
            var methodBlocks = ExtractMethodBlocks(root, filePath, semanticModel, options);
            codeBlocks.AddRange(methodBlocks);

            // Extract class blocks
            var classBlocks = ExtractClassBlocks(root, filePath, semanticModel, options);
            codeBlocks.AddRange(classBlocks);

            // Extract property blocks
            var propertyBlocks = ExtractPropertyBlocks(root, filePath, semanticModel, options);
            codeBlocks.AddRange(propertyBlocks);

            // Extract constructor blocks
            var constructorBlocks = ExtractConstructorBlocks(root, filePath, semanticModel, options);
            codeBlocks.AddRange(constructorBlocks);

            // Extract code block statements
            var statementBlocks = ExtractCodeBlockStatements(root, filePath, semanticModel, options);
            codeBlocks.AddRange(statementBlocks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code blocks from syntax tree for file: {FilePath}", filePath);
        }

        return codeBlocks;
    }

    private List<CodeBlock> ExtractMethodBlocks(
        CompilationUnitSyntax root,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options)
    {
        var methodBlocks = new List<CodeBlock>();

        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methodDeclarations)
        {
            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Methods))
            {
                var codeText = method.GetText().ToString();
                var lineSpan = root.SyntaxTree.GetLineSpan(method.Span);

                var block = new CodeBlock
                {
                    FilePath = filePath,
                    ElementName = method.Identifier.Text,
                    ElementType = CodeElementType.Method,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    StartColumn = lineSpan.StartLinePosition.Character + 1,
                    Code = codeText,
                    Namespace = GetNamespace(method),
                    ContainingType = GetContainingType(method),
                    Accessibility = GetAccessibility(method),
                    Tokens = ExtractTokens(method),
                    AstStructure = ExtractAstStructure(method),
                    CodeHash = ComputeHash(codeText),
                    NormalizedCode = NormalizeCode(codeText, options),
                    Complexity = CalculateComplexity(method)
                };

                methodBlocks.Add(block);
            }
        }

        return methodBlocks;
    }

    private List<CodeBlock> ExtractClassBlocks(
        CompilationUnitSyntax root,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options)
    {
        var classBlocks = new List<CodeBlock>();

        var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classDeclarations)
        {
            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Classes))
            {
                var codeText = classDecl.GetText().ToString();
                var lineSpan = root.SyntaxTree.GetLineSpan(classDecl.Span);

                var block = new CodeBlock
                {
                    FilePath = filePath,
                    ElementName = classDecl.Identifier.Text,
                    ElementType = CodeElementType.Class,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    StartColumn = lineSpan.StartLinePosition.Character + 1,
                    Code = codeText,
                    Namespace = GetNamespace(classDecl),
                    ContainingType = "",
                    Accessibility = GetAccessibility(classDecl),
                    Tokens = ExtractTokens(classDecl),
                    AstStructure = ExtractAstStructure(classDecl),
                    CodeHash = ComputeHash(codeText),
                    NormalizedCode = NormalizeCode(codeText, options),
                    Complexity = CalculateComplexity(classDecl)
                };

                classBlocks.Add(block);
            }
        }

        return classBlocks;
    }

    private List<CodeBlock> ExtractPropertyBlocks(
        CompilationUnitSyntax root,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options)
    {
        var propertyBlocks = new List<CodeBlock>();

        var propertyDeclarations = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        foreach (var property in propertyDeclarations)
        {
            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Properties))
            {
                var codeText = property.GetText().ToString();
                var lineSpan = root.SyntaxTree.GetLineSpan(property.Span);

                var block = new CodeBlock
                {
                    FilePath = filePath,
                    ElementName = property.Identifier.Text,
                    ElementType = CodeElementType.Property,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    StartColumn = lineSpan.StartLinePosition.Character + 1,
                    Code = codeText,
                    Namespace = GetNamespace(property),
                    ContainingType = GetContainingType(property),
                    Accessibility = GetAccessibility(property),
                    Tokens = ExtractTokens(property),
                    AstStructure = ExtractAstStructure(property),
                    CodeHash = ComputeHash(codeText),
                    NormalizedCode = NormalizeCode(codeText, options),
                    Complexity = CalculateComplexity(property)
                };

                propertyBlocks.Add(block);
            }
        }

        return propertyBlocks;
    }

    private List<CodeBlock> ExtractConstructorBlocks(
        CompilationUnitSyntax root,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options)
    {
        var constructorBlocks = new List<CodeBlock>();

        var constructorDeclarations = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

        foreach (var constructor in constructorDeclarations)
        {
            if (options.DetectionTypes.HasFlag(DuplicateDetectionTypes.Constructors))
            {
                var codeText = constructor.GetText().ToString();
                var lineSpan = root.SyntaxTree.GetLineSpan(constructor.Span);

                var block = new CodeBlock
                {
                    FilePath = filePath,
                    ElementName = constructor.Identifier.Text,
                    ElementType = CodeElementType.Constructor,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    StartColumn = lineSpan.StartLinePosition.Character + 1,
                    Code = codeText,
                    Namespace = GetNamespace(constructor),
                    ContainingType = GetContainingType(constructor),
                    Accessibility = GetAccessibility(constructor),
                    Tokens = ExtractTokens(constructor),
                    AstStructure = ExtractAstStructure(constructor),
                    CodeHash = ComputeHash(codeText),
                    NormalizedCode = NormalizeCode(codeText, options),
                    Complexity = CalculateComplexity(constructor)
                };

                constructorBlocks.Add(block);
            }
        }

        return constructorBlocks;
    }

    private List<CodeBlock> ExtractCodeBlockStatements(
        CompilationUnitSyntax root,
        string filePath,
        SemanticModel semanticModel,
        DuplicateDetectionOptions options)
    {
        var blocks = new List<CodeBlock>();

        if (!options.DetectionTypes.HasFlag(DuplicateDetectionTypes.CodeBlocks))
            return blocks;

        var statements = root.DescendantNodes().OfType<StatementSyntax>();

        foreach (var statement in statements)
        {
            // Skip very small blocks
            var codeText = statement.GetText().ToString();
            if (codeText.Length < options.MinBlockSize)
                continue;

            var lineSpan = root.SyntaxTree.GetLineSpan(statement.Span);

            var block = new CodeBlock
            {
                FilePath = filePath,
                ElementName = $"Block_{lineSpan.StartLinePosition.Line}",
                ElementType = CodeElementType.CodeBlock,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                Code = codeText,
                Namespace = GetNamespace(statement),
                ContainingType = GetContainingType(statement),
                Accessibility = MCPsharp.Models.Accessibility.Private,
                Tokens = ExtractTokens(statement),
                AstStructure = ExtractAstStructure(statement),
                CodeHash = ComputeHash(codeText),
                NormalizedCode = NormalizeCode(codeText, options),
                Complexity = CalculateComplexity(statement)
            };

            blocks.Add(block);
        }

        return blocks;
    }

    private async Task<List<CodeBlock>> ExtractCodeBlocksFromFileAsync(
        string filePath,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var codeBlocks = new List<CodeBlock>();

        try
        {
            var code = await File.ReadAllTextAsync(filePath, cancellationToken);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = await tree.GetRootAsync(cancellationToken);

            if (root is CompilationUnitSyntax compilationUnit)
            {
                var methodBlocks = ExtractMethodBlocks(compilationUnit, filePath, null!, options);
                codeBlocks.AddRange(methodBlocks);

                var classBlocks = ExtractClassBlocks(compilationUnit, filePath, null!, options);
                codeBlocks.AddRange(classBlocks);

                var propertyBlocks = ExtractPropertyBlocks(compilationUnit, filePath, null!, options);
                codeBlocks.AddRange(propertyBlocks);

                var constructorBlocks = ExtractConstructorBlocks(compilationUnit, filePath, null!, options);
                codeBlocks.AddRange(constructorBlocks);

                var statementBlocks = ExtractCodeBlockStatements(compilationUnit, filePath, null!, options);
                codeBlocks.AddRange(statementBlocks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code blocks from file: {FilePath}", filePath);
        }

        return codeBlocks;
    }

    private bool ShouldExcludeFile(string filePath, DuplicateDetectionOptions options)
    {
        if (options.ExcludedPaths.Any(pattern => filePath.Contains(pattern)))
            return true;

        if (options.IgnoreGeneratedCode && filePath.Contains("Generated"))
            return true;

        if (options.IgnoreTestCode && (filePath.Contains("Test") || filePath.Contains(".Tests.")))
            return true;

        return false;
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

        return MCPsharp.Models.Accessibility.Private;
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

        if (token.Value != null && DuplicateDetectionUtils.IsNumericType(token.Value.GetType()))
            return TokenType.NumericLiteral;

        if (token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
            return TokenType.Identifier;

        return TokenType.Unknown;
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

        var structuralString = string.Join("|", nodeTypes);
        var structuralHash = DuplicateDetectionUtils.ComputeHash(structuralString);

        var depth = DuplicateDetectionUtils.CalculateMaxDepth(node);
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

    private string NormalizeCode(string code, DuplicateDetectionOptions options)
    {
        return DuplicateDetectionUtils.NormalizeCode(code, options);
    }

    private string ComputeHash(string input)
    {
        return DuplicateDetectionUtils.ComputeHash(input);
    }

    private ComplexityMetrics CalculateComplexity(SyntaxNode node)
    {
        var calculator = new ComplexityCalculator();
        return calculator.CalculateComplexity(node);
    }
}
