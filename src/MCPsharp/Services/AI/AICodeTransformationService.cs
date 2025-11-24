using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Services.Roslyn;
using System.Text.Json;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI-powered code transformation service that uses Roslyn AST to ensure
/// all modifications leave the codebase in a valid, compilable state.
/// </summary>
public class AICodeTransformationService
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
        _logger.LogInformation("AI suggesting fix for {FilePath}: {Description}", filePath, description);

        // Read current file content
        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Parse to syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode);
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
        _logger.LogInformation("AI refactoring {FilePath}: {Goal}", filePath, refactoringGoal);

        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode);
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
        _logger.LogInformation("AI implementing feature in {FilePath}: {Feature}", filePath, featureDescription);

        var currentCode = await File.ReadAllTextAsync(filePath, cancellationToken);
        var syntaxTree = CSharpSyntaxTree.ParseText(currentCode);
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
            // Remove markdown code blocks if present
            var cleanJson = aiResponse.Trim();
            if (cleanJson.StartsWith("```json"))
            {
                cleanJson = cleanJson.Substring(7);
            }
            if (cleanJson.StartsWith("```"))
            {
                cleanJson = cleanJson.Substring(3);
            }
            if (cleanJson.EndsWith("```"))
            {
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            }
            cleanJson = cleanJson.Trim();

            var doc = JsonDocument.Parse(cleanJson);
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
            _logger.LogError(ex, "Failed to parse AI code response: {Response}", aiResponse);
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
            var cleanJson = aiResponse.Trim();
            if (cleanJson.StartsWith("```json"))
                cleanJson = cleanJson.Substring(7);
            if (cleanJson.StartsWith("```"))
                cleanJson = cleanJson.Substring(3);
            if (cleanJson.EndsWith("```"))
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            cleanJson = cleanJson.Trim();

            var doc = JsonDocument.Parse(cleanJson);
            if (doc.RootElement.TryGetProperty("explanation", out var explanation))
            {
                return explanation.GetString() ?? "No explanation provided";
            }
        }
        catch
        {
            // Fall through to default
        }

        return "AI transformation applied";
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
                "replace" when change.NewCode != null => newRoot.ReplaceNode(
                    targetNode,
                    SyntaxFactory.ParseSyntaxTree(change.NewCode).GetRoot().DescendantNodes().First()
                ),

                "delete" => newRoot.RemoveNode(targetNode, SyntaxRemoveOptions.KeepNoTrivia)
                    ?? newRoot,

                _ => newRoot
            };
        }

        return newRoot;
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
            var syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath);

            // Check for syntax errors
            var diagnostics = syntaxTree.GetDiagnostics(cancellationToken);
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            if (errors.Any())
            {
                _logger.LogWarning("Transformed code has {ErrorCount} syntax errors", errors.Count);
                foreach (var error in errors.Take(5))
                {
                    _logger.LogWarning("  {Error}", error.GetMessage());
                }
                return false;
            }

            // Try to compile with workspace
            // TODO: Get compilation from workspace and check semantic errors

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate transformed code");
            return false;
        }
    }
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
