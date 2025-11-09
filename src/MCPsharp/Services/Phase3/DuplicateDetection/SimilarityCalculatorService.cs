using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Service for calculating similarity metrics between code blocks
/// </summary>
public class SimilarityCalculatorService
{
    public async Task<double> CalculateSimilarityAsync(
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

        return await Task.FromResult(overallSimilarity);
    }

    public double CalculateStructuralSimilarity(CodeBlock block1, CodeBlock block2)
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

    public double CalculateTokenSimilarity(CodeBlock block1, CodeBlock block2, DuplicateDetectionOptions options)
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

    public List<CodeToken> FilterTokens(IReadOnlyList<CodeToken> tokens, DuplicateDetectionOptions options)
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

    public int CalculateLevenshteinDistance<T>(IList<T> sequence1, IList<T> sequence2)
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

    public async Task<double> CalculateStructuralSimilarityAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
    {
        return await Task.FromResult(CalculateStructuralSimilarity(codeBlock1, codeBlock2));
    }

    public async Task<double> CalculateTokenSimilarityAsync(
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

        return await Task.FromResult(CalculateTokenSimilarity(codeBlock1, codeBlock2, detectionOptions));
    }

    public async Task<double> CalculateSemanticSimilarityAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
    {
        // TODO: Enhance semantic similarity using Roslyn semantic analysis
        // Future enhancement: Use syntax tree comparison and type analysis
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return await Task.FromResult(0.0);

        if (codeBlock1.ContainingType == codeBlock2.ContainingType)
            return await Task.FromResult(0.9);

        if (codeBlock1.Namespace == codeBlock2.Namespace)
            return await Task.FromResult(0.7);

        return await Task.FromResult(0.5);
    }
}
