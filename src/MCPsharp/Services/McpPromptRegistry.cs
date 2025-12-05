using System.Collections.Concurrent;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Manages MCP prompts that provide usage guidance templates.
/// </summary>
public sealed class McpPromptRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredPrompt> _prompts = new();
    private readonly ILogger<McpPromptRegistry>? _logger;

    private sealed class RegisteredPrompt
    {
        public required McpPrompt Metadata { get; init; }
        public required Func<Dictionary<string, string>?, PromptGetResult> ContentProvider { get; init; }
    }

    public McpPromptRegistry(ILogger<McpPromptRegistry>? logger = null)
    {
        _logger = logger;
        RegisterBuiltInPrompts();
    }

    private void RegisterBuiltInPrompts()
    {
        // Analyze Codebase prompt
        RegisterPrompt(
            new McpPrompt
            {
                Name = "analyze-codebase",
                Description = "Template for comprehensive codebase analysis using MCPsharp semantic tools",
                Arguments = null
            },
            _ => new PromptGetResult
            {
                Description = "Analyze codebase using MCPsharp semantic tools",
                Messages =
                [
                    new PromptMessage
                    {
                        Role = "user",
                        Content = new PromptContent
                        {
                            Type = "text",
                            Text = @"To analyze this codebase effectively, use MCPsharp's semantic tools in this order:

1. **Start with project overview**: Read the `project://overview` resource to understand project structure
2. **Find symbols semantically**: Use `find_symbol` instead of grep to locate classes/methods
3. **Understand relationships**: Use `find_references`, `find_callers`, `find_call_chains` for code flow
4. **Get structured data**: Use `get_class_structure` before reading files to understand members
5. **Ask high-level questions**: Use `ask_codebase` for architectural understanding

Prefer semantic queries over text search:
- Use `find_symbol` instead of grep for code elements
- Use `find_references` instead of text search for symbol usage
- Use `get_class_structure` instead of reading entire files

This approach is faster and more accurate than file-by-file reading."
                        }
                    }
                ]
            });

        // Implement Feature prompt
        RegisterPrompt(
            new McpPrompt
            {
                Name = "implement-feature",
                Description = "Template for implementing new features using MCPsharp",
                Arguments =
                [
                    new McpPromptArgument { Name = "feature", Description = "Feature to implement", Required = true }
                ]
            },
            args => new PromptGetResult
            {
                Description = "Implement a feature using MCPsharp semantic tools",
                Messages =
                [
                    new PromptMessage
                    {
                        Role = "user",
                        Content = new PromptContent
                        {
                            Type = "text",
                            Text = $@"To implement '{args?.GetValueOrDefault("feature", "[feature]")}', follow this workflow:

1. **Understand context**: Use `ask_codebase` to understand related existing code
2. **Find integration points**: Use `find_symbol` to locate where to add new code
3. **Analyze dependencies**: Use `find_callers` and `find_references` to understand impact
4. **Get class structure**: Use `get_class_structure` before modifying any class
5. **Use semantic editing**: Prefer `ai_implement_feature` for Roslyn-safe code generation

Key semantic tools:
- `find_symbol` - locate symbols by name
- `find_references` - find all usages
- `get_class_structure` - understand class members
- `ai_implement_feature` - generate syntactically valid code"
                        }
                    }
                ]
            });

        // Fix Bug prompt
        RegisterPrompt(
            new McpPrompt
            {
                Name = "fix-bug",
                Description = "Template for debugging and fixing bugs using MCPsharp",
                Arguments =
                [
                    new McpPromptArgument { Name = "issue", Description = "Bug description", Required = true }
                ]
            },
            args => new PromptGetResult
            {
                Description = "Debug and fix using MCPsharp semantic tools",
                Messages =
                [
                    new PromptMessage
                    {
                        Role = "user",
                        Content = new PromptContent
                        {
                            Type = "text",
                            Text = $@"To fix '{args?.GetValueOrDefault("issue", "[bug]")}', use this approach:

1. **Locate the issue**: Use `find_symbol` to find related code
2. **Trace execution**: Use `find_call_chains` to understand code flow
3. **Check callers**: Use `find_callers` to see how the buggy code is invoked
4. **Analyze structure**: Use `get_class_structure` to understand the affected class
5. **Apply fix**: Use `ai_suggest_fix` for Roslyn-safe fix suggestions

Debugging tools:
- `find_call_chains` - trace backward from error
- `find_callers` - who invokes problematic code
- `ai_suggest_fix` - generate syntactically valid fixes"
                        }
                    }
                ]
            });
    }

    /// <summary>
    /// Registers a prompt with a content provider.
    /// </summary>
    public void RegisterPrompt(McpPrompt prompt, Func<Dictionary<string, string>?, PromptGetResult> contentProvider)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(contentProvider);

        var registered = new RegisteredPrompt
        {
            Metadata = prompt,
            ContentProvider = contentProvider
        };

        _prompts[prompt.Name] = registered;
        _logger?.LogDebug("Registered prompt: {Name}", prompt.Name);
    }

    /// <summary>
    /// Lists all registered prompts.
    /// </summary>
    public PromptListResult ListPrompts()
    {
        var prompts = _prompts.Values
            .Select(p => p.Metadata)
            .OrderBy(p => p.Name)
            .ToList();

        return new PromptListResult
        {
            Prompts = prompts
        };
    }

    /// <summary>
    /// Gets a prompt by name with optional argument substitution.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if prompt not found.</exception>
    public PromptGetResult GetPrompt(string name, Dictionary<string, string>? arguments = null)
    {
        if (!_prompts.TryGetValue(name, out var prompt))
        {
            throw new KeyNotFoundException($"Prompt not found: {name}");
        }

        return prompt.ContentProvider(arguments);
    }

    /// <summary>
    /// Checks if a prompt exists.
    /// </summary>
    public bool HasPrompt(string name) => _prompts.ContainsKey(name);

    /// <summary>
    /// Gets the count of registered prompts.
    /// </summary>
    public int Count => _prompts.Count;
}
