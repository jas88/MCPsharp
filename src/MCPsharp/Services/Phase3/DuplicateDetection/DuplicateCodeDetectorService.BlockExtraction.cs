using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Extraction of statement, property and constructor code blocks
/// </summary>
public partial class DuplicateCodeDetectorService
{
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
            if (property.AccessorList == null)
                continue;

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
            if (constructor.Body == null)
                continue;

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
}
