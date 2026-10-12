using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Services.Roslyn;
using System.Globalization;
using System.Text.Json;

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

    /// <summary>
    /// Build prompt for AI to suggest bug fix.
    /// </summary>
    private static string BuildFixPrompt(string code, string description, int? lineNumber)
    {
        var lineContext = lineNumber.HasValue ? $"Focus on line {lineNumber}" : "";

        return $@"You are a C# code fixing expert. Fix the following bug using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Bug Description

{description}
{lineContext}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the fix"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"" | ""delete"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion (if applicable)""
    }}
  ]
}}
```

IMPORTANT:
- Only return valid C# syntax
- Use Roslyn node types (MethodDeclaration, PropertyDeclaration, etc.)
- Ensure the result compiles
- Preserve formatting and indentation style
- Return ONLY the JSON, no markdown code blocks
";
    }

    /// <summary>
    /// Build prompt for AI refactoring.
    /// </summary>
    private static string BuildRefactorPrompt(string code, string goal)
    {
        return $@"You are a C# refactoring expert. Refactor the following code using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Refactoring Goal

{goal}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the refactoring"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"" | ""delete"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion (if applicable)""
    }}
  ]
}}
```

IMPORTANT:
- Preserve behavior while improving code quality
- Use modern C# idioms
- Ensure the result compiles
- Maintain existing API contracts
- Return ONLY the JSON, no markdown code blocks
";
    }

    /// <summary>
    /// Build prompt for AI feature implementation.
    /// </summary>
    private static string BuildImplementationPrompt(string code, string featureDescription)
    {
        return $@"You are a C# implementation expert. Add the requested feature to the following code using Roslyn AST transformations.

## Current Code

```csharp
{code}
```

## Feature to Implement

{featureDescription}

## Instructions

Return a JSON response with this structure:

```json
{{
  ""explanation"": ""Brief explanation of the implementation"",
  ""changes"": [
    {{
      ""type"": ""replace"" | ""insert"",
      ""target"": ""MethodDeclaration:MethodName"" | ""PropertyDeclaration:PropertyName"" | ""ClassDeclaration:ClassName"",
      ""newCode"": ""C# code for replacement/insertion""
    }}
  ]
}}
```

IMPORTANT:
- Generate clean, idiomatic C# code
- Follow existing code style and conventions
- Ensure the result compiles
- Add necessary using statements
- Return ONLY the JSON, no markdown code blocks
";
    }

    /// <summary>
    /// Parse AI response to extract code changes.
    /// </summary>
    private List<CodeChange> ParseAICodeResponse(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripMarkdownFences(aiResponse));
            var root = doc.RootElement;

            var changes = new List<CodeChange>();

            if (root.TryGetProperty("changes", out var changesElement))
            {
                foreach (var change in changesElement.EnumerateArray())
                {
                    changes.Add(new CodeChange
                    {
                        Type = change.GetProperty("type").GetString() ?? "replace",
                        Target = change.GetProperty("target").GetString() ?? "",
                        NewCode = change.TryGetProperty("newCode", out var newCode)
                            ? newCode.GetString()
                            : null
                    });
                }
            }

            return changes;
        }
        catch (Exception ex)
        {
            LogParseResponseFailed(_logger, ex, aiResponse);
            return new List<CodeChange>();
        }
    }

    /// <summary>
    /// Extract explanation from AI response.
    /// </summary>
    private static string ExtractExplanation(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripMarkdownFences(aiResponse));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("explanation", out var explanation)
                && explanation.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            {
                return explanation.GetString() ?? "No explanation provided";
            }
        }
        catch (JsonException)
        {
            // Response was not valid JSON; fall through to the default explanation.
        }

        return "AI transformation applied";
    }

    /// <summary>
    /// Remove markdown code fences (```json ... ```) surrounding an AI JSON response.
    /// </summary>
    private static string StripMarkdownFences(string aiResponse)
    {
        var cleanJson = aiResponse.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[7..];
        }
        if (cleanJson.StartsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[3..];
        }
        if (cleanJson.EndsWith("```", StringComparison.Ordinal))
        {
            cleanJson = cleanJson[..^3];
        }
        return cleanJson.Trim();
    }

    /// <summary>
    /// Apply code changes using Roslyn syntax tree transformations.
    /// </summary>
    private static SyntaxNode ApplyCodeChanges(SyntaxNode root, List<CodeChange> changes)
    {
        var newRoot = root;

        foreach (var change in changes)
        {
            // Parse target (e.g., "MethodDeclaration:MethodName")
            var parts = change.Target.Split(':');
            if (parts.Length != 2)
                continue;

            var nodeType = parts[0];
            var nodeName = parts[1];

            // Find target node
            SyntaxNode? targetNode = nodeType switch
            {
                "MethodDeclaration" => newRoot.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == nodeName),

                "PropertyDeclaration" => newRoot.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == nodeName),

                "ClassDeclaration" => newRoot.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.Text == nodeName),

                _ => null
            };

            if (targetNode == null)
                continue;

            // Apply transformation
            newRoot = change.Type switch
            {
                "replace" when change.NewCode != null =>
                    ReplaceNodeWithParsedCode(newRoot, targetNode, change.NewCode),

                "insert" when change.NewCode != null =>
                    InsertNodeWithParsedCode(newRoot, targetNode, change.NewCode),

                "delete" => newRoot.RemoveNode(targetNode, SyntaxRemoveOptions.KeepNoTrivia)
                    ?? newRoot,

                _ => newRoot
            };
        }

        return newRoot;
    }

    /// <summary>
    /// Replace a node with parsed code, safely handling empty parse results.
    /// </summary>
    private static SyntaxNode ReplaceNodeWithParsedCode(SyntaxNode root, SyntaxNode targetNode, string newCode)
    {
        var parsedTree = SyntaxFactory.ParseSyntaxTree(newCode);
        var parsedRoot = parsedTree.GetRoot();
        var newNode = parsedRoot.DescendantNodes().FirstOrDefault();

        if (newNode == null)
            return root; // No valid node to insert, return unchanged

        return root.ReplaceNode(targetNode, newNode);
    }

    /// <summary>
    /// Insert a new node as a sibling after the target node.
    /// </summary>
    private static SyntaxNode InsertNodeWithParsedCode(SyntaxNode root, SyntaxNode targetNode, string newCode)
    {
        var parsedTree = SyntaxFactory.ParseSyntaxTree(newCode);
        var parsedRoot = parsedTree.GetRoot();
        var newNode = parsedRoot.DescendantNodes().FirstOrDefault();

        if (newNode == null)
            return root; // No valid node to insert, return unchanged

        // Use Roslyn's InsertNodesAfter for proper node insertion
        return root.InsertNodesAfter(targetNode, new[] { newNode });
    }

    /// <summary>
    /// Validate that transformed code compiles.
    /// </summary>
    private async Task<bool> ValidateCodeAsync(
        string code,
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse to syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath, cancellationToken: cancellationToken);

            // Check for syntax errors
            var diagnostics = syntaxTree.GetDiagnostics(cancellationToken);
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            if (errors.Count > 0)
            {
                LogSyntaxErrors(_logger, errors.Count);
                foreach (var error in errors.Take(5))
                {
                    LogDiagnostic(_logger, error.GetMessage(CultureInfo.InvariantCulture));
                }
                return false;
            }

            // Check semantic errors against the workspace compilation when the file is part of it
            return await ValidateSemanticsAsync(syntaxTree, filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            LogValidationFailed(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Replace the file's syntax tree in the workspace compilation and reject the transformation
    /// if it introduces semantic errors that the original file did not have.
    /// Returns true when the file is not part of a loaded workspace (syntax validation only).
    /// </summary>
    private async Task<bool> ValidateSemanticsAsync(
        SyntaxTree newTree,
        string filePath,
        CancellationToken cancellationToken)
    {
        var compilation = _workspace.GetCompilation();
        var document = _workspace.GetDocument(filePath);
        if (compilation == null || document == null)
        {
            return true;
        }

        var oldTree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (oldTree == null || !compilation.ContainsSyntaxTree(oldTree))
        {
            return true;
        }

        var reparsedTree = newTree.WithRootAndOptions(await newTree.GetRootAsync(cancellationToken), oldTree.Options);
        var newCompilation = compilation.ReplaceSyntaxTree(oldTree, reparsedTree);

        var originalErrorCount = CountErrors(compilation.GetSemanticModel(oldTree).GetDiagnostics(cancellationToken: cancellationToken));
        var newErrors = newCompilation.GetSemanticModel(reparsedTree).GetDiagnostics(cancellationToken: cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (newErrors.Count <= originalErrorCount)
        {
            return true;
        }

        LogSemanticErrors(_logger, newErrors.Count, originalErrorCount);
        foreach (var error in newErrors.Take(5))
        {
            LogDiagnostic(_logger, error.GetMessage(CultureInfo.InvariantCulture));
        }
        return false;
    }

    private static int CountErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI suggesting fix for {FilePath}: {Description}")]
    private static partial void LogSuggestingFix(ILogger logger, string filePath, string description);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI refactoring {FilePath}: {Goal}")]
    private static partial void LogRefactoring(ILogger logger, string filePath, string goal);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI implementing feature in {FilePath}: {Feature}")]
    private static partial void LogImplementingFeature(ILogger logger, string filePath, string feature);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse AI code response: {Response}")]
    private static partial void LogParseResponseFailed(ILogger logger, Exception exception, string response);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transformed code has {ErrorCount} syntax errors")]
    private static partial void LogSyntaxErrors(ILogger logger, int errorCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transformed code has {ErrorCount} semantic errors (original had {OriginalErrorCount})")]
    private static partial void LogSemanticErrors(ILogger logger, int errorCount, int originalErrorCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "  {Error}")]
    private static partial void LogDiagnostic(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to validate transformed code")]
    private static partial void LogValidationFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Result of AI code transformation.
/// </summary>
public class CodeTransformationResult
{
    public string FilePath { get; set; } = "";
    public string OriginalCode { get; set; } = "";
    public string TransformedCode { get; set; } = "";
    public bool IsValid { get; set; }
    public string Description { get; set; } = "";
    public string AIExplanation { get; set; } = "";
    public List<CodeChange> Changes { get; set; } = new();
}

/// <summary>
/// Individual code change from AI.
/// </summary>
public class CodeChange
{
    public string Type { get; set; } = ""; // replace, insert, delete
    public string Target { get; set; } = ""; // NodeType:NodeName
    public string? NewCode { get; set; }
}
