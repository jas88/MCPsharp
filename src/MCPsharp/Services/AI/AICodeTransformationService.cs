using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI-powered code transformation service that uses Roslyn AST to ensure
/// all modifications leave the codebase in a valid, compilable state.
/// </summary>
public partial class AICodeTransformationService
{
    private readonly IAIProvider _aiProvider;
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<AICodeTransformationService> _logger;

    public AICodeTransformationService(
        IAIProvider aiProvider,
        RoslynWorkspace workspace,
        ILogger<AICodeTransformationService> logger)
    {
        _aiProvider = aiProvider;
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// AI suggests a fix for a bug, returns Roslyn-based transformations
    /// that are guaranteed to be syntactically valid.
    /// </summary>
    public async Task<CodeTransformationResult> SuggestFixAsync(
        string filePath,
        string description,
        int? lineNumber = null,
        CancellationToken cancellationToken = default)
    {
        LogSuggestingFix(_logger, filePath, description);

        // Read current file content
        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Parse to syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode, cancellationToken: cancellationToken);
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        // Build AI prompt with context
        var prompt = BuildFixPrompt(currentCode, description, lineNumber);

        // Get AI suggestion
        var aiResponse = await _aiProvider.ProcessQueryAsync(prompt, cancellationToken);

        // Parse AI response to extract code changes
        var codeChanges = ParseAICodeResponse(aiResponse);

        // Apply changes using Roslyn rewriter
        var newRoot = ApplyCodeChanges(root, codeChanges);

        // Validate result compiles
        var newCode = newRoot.ToFullString();
        var isValid = await ValidateCodeAsync(newCode, filePath, cancellationToken);

        return new CodeTransformationResult
        {
            FilePath = filePath,
            OriginalCode = currentCode,
            TransformedCode = newCode,
            IsValid = isValid,
            Description = description,
            AIExplanation = ExtractExplanation(aiResponse),
            Changes = codeChanges
        };
    }

    /// <summary>
    /// AI-guided refactoring with semantic awareness.
    /// </summary>
    public async Task<CodeTransformationResult> RefactorAsync(
        string filePath,
        string refactoringGoal,
        CancellationToken cancellationToken = default)
    {
        LogRefactoring(_logger, filePath, refactoringGoal);

        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode, cancellationToken: cancellationToken);
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        var prompt = BuildRefactorPrompt(currentCode, refactoringGoal);
        var aiResponse = await _aiProvider.ProcessQueryAsync(prompt, cancellationToken);

        var codeChanges = ParseAICodeResponse(aiResponse);
        var newRoot = ApplyCodeChanges(root, codeChanges);

        var newCode = newRoot.ToFullString();
        var isValid = await ValidateCodeAsync(newCode, filePath, cancellationToken);

        return new CodeTransformationResult
        {
            FilePath = filePath,
            OriginalCode = currentCode,
            TransformedCode = newCode,
            IsValid = isValid,
            Description = refactoringGoal,
            AIExplanation = ExtractExplanation(aiResponse),
            Changes = codeChanges
        };
    }

    /// <summary>
    /// AI implements a feature based on natural language description.
    /// </summary>
    public async Task<CodeTransformationResult> ImplementFeatureAsync(
        string filePath,
        string featureDescription,
        CancellationToken cancellationToken = default)
    {
        LogImplementingFeature(_logger, filePath, featureDescription);

        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode, cancellationToken: cancellationToken);
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        var prompt = BuildImplementationPrompt(currentCode, featureDescription);
        var aiResponse = await _aiProvider.ProcessQueryAsync(prompt, cancellationToken);

        var codeChanges = ParseAICodeResponse(aiResponse);
        var newRoot = ApplyCodeChanges(root, codeChanges);

        var newCode = newRoot.ToFullString();
        var isValid = await ValidateCodeAsync(newCode, filePath, cancellationToken);

        return new CodeTransformationResult
        {
            FilePath = filePath,
            OriginalCode = currentCode,
            TransformedCode = newCode,
            IsValid = isValid,
            Description = featureDescription,
            AIExplanation = ExtractExplanation(aiResponse),
            Changes = codeChanges
        };
    }
}
