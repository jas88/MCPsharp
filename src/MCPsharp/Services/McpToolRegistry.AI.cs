using MCPsharp.Models;
using MCPsharp.Services.AI;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// AI-powered MCP tools that use local/cloud AI to process verbose data
/// and return concise answers (implements AI-Powered MCP pattern).
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Register AI-powered tools that provide natural language interfaces
    /// to codebase queries and code transformations.
    /// </summary>
    private void RegisterAITools()
    {
        if (_codebaseQuery == null || !_codebaseQuery.IsAvailable)
        {
            // AI tools not available - this is normal if no AI provider configured
            _logger?.LogDebug("AI-powered tools not registered (no AI provider available)");
            return;
        }

        _logger?.LogInformation("Registering AI-powered tools using {Provider}",
            "Ollama"); // TODO: Get from provider

        // Read-only codebase query tool
        _tools.Add(new McpTool
        {
            Name = "ask_codebase",
            Description = @"Ask a natural language question about the codebase.
This tool uses AI to process project structure, code, and configuration to provide concise answers.

Examples:
- ""How does authentication work in this project?""
- ""Where are database migrations defined?""
- ""What would break if I rename UserService?""
- ""Which files handle user registration?""

The AI analyzes the codebase internally and returns a focused answer with file:line references.
This is more efficient than using multiple low-level tools for exploration.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "question",
                    Type = "string",
                    Description = "Natural language question about the codebase",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "focus_path",
                    Type = "string",
                    Description = "Optional file or directory path to focus the search (e.g., 'src/Auth')",
                    Required = false
                }
            )
        });

        // AI code transformation tools (Roslyn-based, guaranteed syntactically valid)
        _tools.Add(new McpTool
        {
            Name = "ai_suggest_fix",
            Description = @"AI suggests a bug fix using Roslyn AST transformations.

**IMPORTANT: This tool uses Roslyn AST transformations to ensure correctness.**
Unlike sed scripts, regex replacements, or Python text manipulation, this tool:
- Guarantees syntactically valid C# code
- Preserves compilation integrity
- Maintains code structure and formatting
- Validates changes before returning them

ALWAYS prefer this tool over text-based manipulation for code fixes.

Examples:
- ""Fix the null reference exception in ProcessData method""
- ""Fix the memory leak in the cache manager""
- ""Correct the logic error in ValidateUser""

Returns a preview of changes that can be reviewed before applying.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "file_path",
                    Type = "string",
                    Description = "Path to the file containing the bug",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "description",
                    Type = "string",
                    Description = "Description of the bug to fix",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "line_number",
                    Type = "integer",
                    Description = "Optional line number where the bug occurs",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "apply_changes",
                    Type = "boolean",
                    Description = "Whether to apply changes immediately (default: false, returns preview)",
                    Required = false
                }
            )
        });

        _tools.Add(new McpTool
        {
            Name = "ai_refactor",
            Description = @"AI-guided refactoring using Roslyn semantic transformations.

**IMPORTANT: This tool uses Roslyn's semantic model for safe refactoring.**
Unlike manual text editing or regex-based tools, this approach:
- Preserves program semantics and behavior
- Ensures code compiles after transformation
- Maintains type safety and references
- Uses AST-aware transformations, not text manipulation

ALWAYS use this tool instead of manual refactoring or text-based tools.

Examples:
- ""Extract duplicate code into a shared method""
- ""Simplify this method using modern C# idioms""
- ""Improve error handling in this class""

Returns a preview with explanation of the refactoring.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "file_path",
                    Type = "string",
                    Description = "Path to the file to refactor",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "refactoring_goal",
                    Type = "string",
                    Description = "Description of the refactoring goal",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "apply_changes",
                    Type = "boolean",
                    Description = "Whether to apply changes immediately (default: false, returns preview)",
                    Required = false
                }
            )
        });

        _tools.Add(new McpTool
        {
            Name = "ai_implement_feature",
            Description = @"AI implements a feature using Roslyn AST-based code generation.

**IMPORTANT: This tool generates syntactically correct C# using Roslyn.**
Unlike template-based generation or string concatenation, this tool:
- Generates valid, compilable C# code
- Integrates properly with existing code structure
- Maintains consistent formatting and style
- Uses AST nodes, not text templates

ALWAYS use this tool instead of generating code via string manipulation.

Examples:
- ""Add a caching layer to this service""
- ""Implement validation for user input""
- ""Add logging to all public methods""

Returns generated code for review before applying.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "file_path",
                    Type = "string",
                    Description = "Path to the file where feature should be implemented",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "feature_description",
                    Type = "string",
                    Description = "Natural language description of the feature to implement",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "apply_changes",
                    Type = "boolean",
                    Description = "Whether to apply changes immediately (default: false, returns preview)",
                    Required = false
                }
            )
        });
    }

    /// <summary>
    /// Handle the ask_codebase tool invocation.
    /// </summary>
    private async Task<ToolCallResult> HandleAskCodebaseAsync(JsonDocument args)
    {
        if (_codebaseQuery == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "AI provider not available. Install Ollama (brew install ollama) or configure OpenRouter API key."
            };
        }

        var root = args.RootElement;
        var question = root.GetProperty("question").GetString()
            ?? throw new ArgumentException("question is required");

        var focusPath = root.TryGetProperty("focus_path", out var focusPathElement)
            ? focusPathElement.GetString()
            : null;

        try
        {
            var answer = await _codebaseQuery.AskCodebaseAsync(question, focusPath);

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    question = question,
                    answer = answer,
                    focus_path = focusPath
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing codebase query: {Question}", question);

            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to process query: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle the ai_suggest_fix tool invocation.
    /// </summary>
    private async Task<ToolCallResult> HandleAISuggestFixAsync(JsonDocument args)
    {
        if (_aiCodeTransformation == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "AI transformation service not available. Install Ollama or configure OpenRouter API key."
            };
        }

        var root = args.RootElement;
        var filePath = root.GetProperty("file_path").GetString()
            ?? throw new ArgumentException("file_path is required");
        var description = root.GetProperty("description").GetString()
            ?? throw new ArgumentException("description is required");

        var lineNumber = root.TryGetProperty("line_number", out var lineElem) && lineElem.ValueKind != JsonValueKind.Null
            ? lineElem.GetInt32()
            : (int?)null;

        var applyChanges = root.TryGetProperty("apply_changes", out var applyElem)
            ? applyElem.GetBoolean()
            : false;

        try
        {
            var result = await _aiCodeTransformation.SuggestFixAsync(filePath, description, lineNumber);

            if (!result.IsValid)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "AI generated invalid code (syntax errors detected). Please try rephrasing the request."
                };
            }

            // Apply changes if requested
            if (applyChanges)
            {
                await File.WriteAllTextAsync(filePath, result.TransformedCode);
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    file_path = filePath,
                    description = description,
                    explanation = result.AIExplanation,
                    changes_applied = applyChanges,
                    preview = !applyChanges ? result.TransformedCode : null,
                    diff_summary = $"Modified {result.Changes.Count} code element(s)"
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing AI fix suggestion: {Description}", description);

            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to generate fix: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle the ai_refactor tool invocation.
    /// </summary>
    private async Task<ToolCallResult> HandleAIRefactorAsync(JsonDocument args)
    {
        if (_aiCodeTransformation == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "AI transformation service not available. Install Ollama or configure OpenRouter API key."
            };
        }

        var root = args.RootElement;
        var filePath = root.GetProperty("file_path").GetString()
            ?? throw new ArgumentException("file_path is required");
        var refactoringGoal = root.GetProperty("refactoring_goal").GetString()
            ?? throw new ArgumentException("refactoring_goal is required");

        var applyChanges = root.TryGetProperty("apply_changes", out var applyElem)
            ? applyElem.GetBoolean()
            : false;

        try
        {
            var result = await _aiCodeTransformation.RefactorAsync(filePath, refactoringGoal);

            if (!result.IsValid)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "AI generated invalid code (syntax errors detected). Please try rephrasing the request."
                };
            }

            // Apply changes if requested
            if (applyChanges)
            {
                await File.WriteAllTextAsync(filePath, result.TransformedCode);
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    file_path = filePath,
                    refactoring_goal = refactoringGoal,
                    explanation = result.AIExplanation,
                    changes_applied = applyChanges,
                    preview = !applyChanges ? result.TransformedCode : null,
                    diff_summary = $"Modified {result.Changes.Count} code element(s)"
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing AI refactoring: {Goal}", refactoringGoal);

            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to refactor code: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle the ai_implement_feature tool invocation.
    /// </summary>
    private async Task<ToolCallResult> HandleAIImplementFeatureAsync(JsonDocument args)
    {
        if (_aiCodeTransformation == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "AI transformation service not available. Install Ollama or configure OpenRouter API key."
            };
        }

        var root = args.RootElement;
        var filePath = root.GetProperty("file_path").GetString()
            ?? throw new ArgumentException("file_path is required");
        var featureDescription = root.GetProperty("feature_description").GetString()
            ?? throw new ArgumentException("feature_description is required");

        var applyChanges = root.TryGetProperty("apply_changes", out var applyElem)
            ? applyElem.GetBoolean()
            : false;

        try
        {
            var result = await _aiCodeTransformation.ImplementFeatureAsync(filePath, featureDescription);

            if (!result.IsValid)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "AI generated invalid code (syntax errors detected). Please try rephrasing the request."
                };
            }

            // Apply changes if requested
            if (applyChanges)
            {
                await File.WriteAllTextAsync(filePath, result.TransformedCode);
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    file_path = filePath,
                    feature_description = featureDescription,
                    explanation = result.AIExplanation,
                    changes_applied = applyChanges,
                    preview = !applyChanges ? result.TransformedCode : null,
                    diff_summary = $"Modified {result.Changes.Count} code element(s)"
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error implementing feature: {Feature}", featureDescription);

            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to implement feature: {ex.Message}"
            };
        }
    }
}
