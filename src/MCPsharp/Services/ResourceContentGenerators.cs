using System.Text;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Generates content for MCP resources.
/// </summary>
public sealed class ResourceContentGenerators
{
    private readonly ProjectContextManager _projectContext;
#pragma warning disable S4487 // Field reserved for future database statistics integration
    private readonly Func<Database.ProjectDatabase?> _getDatabaseFunc;
#pragma warning restore S4487

    public ResourceContentGenerators(
        ProjectContextManager projectContext,
        Func<Database.ProjectDatabase?> getDatabaseFunc)
    {
        _projectContext = projectContext;
        _getDatabaseFunc = getDatabaseFunc;
    }

    /// <summary>
    /// Generates the project overview resource content.
    /// </summary>
    public McpResourceContent GenerateOverview()
    {
        var sb = new StringBuilder();
        var info = _projectContext.GetProjectInfo();

        sb.AppendLine("# Project Overview");
        sb.AppendLine();

        if (info == null || info.Count == 0 || !info.ContainsKey("root_path"))
        {
            sb.AppendLine("*No project currently open.*");
            sb.AppendLine();
            sb.AppendLine("Use `project_open` tool to open a project, or the server will auto-open on startup.");
        }
        else
        {
            sb.AppendLine($"**Project:** {info.GetValueOrDefault("name", "Unknown")}");
            sb.AppendLine($"**Root:** `{info.GetValueOrDefault("root_path", string.Empty)}`");
            sb.AppendLine($"**Files:** {info.GetValueOrDefault("file_count", "0")}");

            if (info.TryGetValue("opened_at", out var openedAt) && openedAt != null)
            {
                sb.AppendLine($"**Opened:** {openedAt}");
            }

            sb.AppendLine();
            sb.AppendLine("## Quick Start");
            sb.AppendLine();
            sb.AppendLine("Use MCPsharp's semantic tools for efficient codebase exploration:");
            sb.AppendLine();
            sb.AppendLine("1. **Find symbols**: `find_symbol` - search for classes, methods, properties");
            sb.AppendLine("2. **Trace references**: `find_references` - see where code is used");
            sb.AppendLine("3. **Call analysis**: `find_callers`, `find_call_chains` - understand code flow");
            sb.AppendLine("4. **Class details**: `get_class_structure` - get member details before editing");
            sb.AppendLine("5. **AI questions**: `ask_codebase` - high-level architectural queries");
            sb.AppendLine();
            sb.AppendLine("> **Tip**: Prefer semantic tools over reading files directly for faster, more accurate results.");
        }

        return new McpResourceContent
        {
            Uri = "project://overview",
            MimeType = "text/markdown",
            Text = sb.ToString()
        };
    }

    /// <summary>
    /// Generates the project structure resource content.
    /// </summary>
    public McpResourceContent GenerateStructure()
    {
        var sb = new StringBuilder();
        var context = _projectContext.GetProjectContext();

        sb.AppendLine("# Project Structure");
        sb.AppendLine();

        if (context == null)
        {
            sb.AppendLine("*No project currently open.*");
        }
        else
        {
            // Group files by extension
            var filesByExtension = context.KnownFiles
                .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Take(15);

            sb.AppendLine("## File Summary");
            sb.AppendLine();
            sb.AppendLine("| Extension | Count |");
            sb.AppendLine("|-----------|-------|");

            foreach (var group in filesByExtension)
            {
                var ext = string.IsNullOrEmpty(group.Key) ? "(no extension)" : group.Key;
                sb.AppendLine($"| {ext} | {group.Count()} |");
            }

            // Top-level directories
            sb.AppendLine();
            sb.AppendLine("## Top-Level Directories");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(context.RootPath))
            {
                var topDirs = context.KnownFiles
                    .Select(f => GetTopLevelDirectory(f, context.RootPath))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .OrderBy(d => d)
                    .Take(20);

                foreach (var dir in topDirs)
                {
                    var count = context.KnownFiles.Count(f => f.StartsWith(dir + Path.DirectorySeparatorChar) || f.StartsWith(dir + "/"));
                    sb.AppendLine($"- `{dir}/` ({count} files)");
                }
            }
        }

        return new McpResourceContent
        {
            Uri = "project://structure",
            MimeType = "text/markdown",
            Text = sb.ToString()
        };
    }

    /// <summary>
    /// Generates the dependencies resource content.
    /// </summary>
    public McpResourceContent GenerateDependencies()
    {
        var sb = new StringBuilder();
        var context = _projectContext.GetProjectContext();

        sb.AppendLine("# Project Dependencies");
        sb.AppendLine();

        if (context == null)
        {
            sb.AppendLine("*No project currently open.*");
        }
        else
        {
            // Find .csproj files
            var projectFiles = context.KnownFiles
                .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .ToList();

            sb.AppendLine($"## Projects ({projectFiles.Count})");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(context.RootPath))
            {
                foreach (var proj in projectFiles.Take(20))
                {
                    var relativePath = Path.GetRelativePath(context.RootPath, proj);
                    sb.AppendLine($"- `{relativePath}`");
                }

                if (projectFiles.Count > 20)
                {
                    sb.AppendLine($"- ... and {projectFiles.Count - 20} more");
                }

                // Find solution files
                var solutionFiles = context.KnownFiles
                    .Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (solutionFiles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"## Solutions ({solutionFiles.Count})");
                    sb.AppendLine();

                    foreach (var sln in solutionFiles)
                    {
                        var relativePath = Path.GetRelativePath(context.RootPath, sln);
                        sb.AppendLine($"- `{relativePath}`");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("> Use `parse_project` tool to get detailed NuGet and project references for a specific .csproj");
        }

        return new McpResourceContent
        {
            Uri = "project://dependencies",
            MimeType = "text/markdown",
            Text = sb.ToString()
        };
    }

    /// <summary>
    /// Generates the symbols summary resource content.
    /// </summary>
    public McpResourceContent GenerateSymbolsSummary()
    {
        var sb = new StringBuilder();
        var context = _projectContext.GetProjectContext();

        sb.AppendLine("# Symbols Summary");
        sb.AppendLine();

        if (context == null)
        {
            sb.AppendLine("*No project currently open.*");
        }
        else
        {
            var csFiles = context.KnownFiles
                .Count(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

            sb.AppendLine($"**C# Source Files:** {csFiles}");
            sb.AppendLine();
            sb.AppendLine("## Finding Symbols");
            sb.AppendLine();
            sb.AppendLine("Use these tools to explore symbols:");
            sb.AppendLine();
            sb.AppendLine("- `find_symbol` - Search by name (supports partial matching)");
            sb.AppendLine("- `get_class_structure` - Get full class details");
            sb.AppendLine("- `find_implementations` - Find interface implementations");
            sb.AppendLine("- `analyze_inheritance` - Explore type hierarchy");
            sb.AppendLine();
            sb.AppendLine("Example queries:");
            sb.AppendLine("```");
            sb.AppendLine("find_symbol(name=\"Service\", kind=\"class\")");
            sb.AppendLine("find_symbol(name=\"Process\", kind=\"method\")");
            sb.AppendLine("get_class_structure(className=\"MyController\")");
            sb.AppendLine("```");
        }

        return new McpResourceContent
        {
            Uri = "project://symbols",
            MimeType = "text/markdown",
            Text = sb.ToString()
        };
    }

    /// <summary>
    /// Generates the usage guidance resource content.
    /// </summary>
    public McpResourceContent GenerateGuidance()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# MCPsharp Usage Guide");
        sb.AppendLine();
        sb.AppendLine("## Best Practices");
        sb.AppendLine();
        sb.AppendLine("### Prefer Semantic Tools Over File Reading");
        sb.AppendLine();
        sb.AppendLine("Instead of reading files directly, use semantic tools that understand code structure:");
        sb.AppendLine();
        sb.AppendLine("| Instead of... | Use... |");
        sb.AppendLine("|--------------|--------|");
        sb.AppendLine("| `grep` for class names | `find_symbol(kind=\"class\")` |");
        sb.AppendLine("| Reading file to find methods | `get_class_structure` |");
        sb.AppendLine("| Text search for function calls | `find_references` or `find_callers` |");
        sb.AppendLine("| Manual code tracing | `find_call_chains` |");
        sb.AppendLine();
        sb.AppendLine("### Recommended Workflow");
        sb.AppendLine();
        sb.AppendLine("1. **Understand first**: Use `ask_codebase` for high-level questions");
        sb.AppendLine("2. **Locate code**: Use `find_symbol` to find relevant code");
        sb.AppendLine("3. **Understand structure**: Use `get_class_structure` before modifications");
        sb.AppendLine("4. **Trace dependencies**: Use `find_references` and `find_callers`");
        sb.AppendLine("5. **Make changes**: Use AI tools (`ai_suggest_fix`, `ai_refactor`) for safe edits");
        sb.AppendLine();
        sb.AppendLine("### When to Read Files");
        sb.AppendLine();
        sb.AppendLine("Use `file_read` when you need:");
        sb.AppendLine("- Full file context after semantic analysis");
        sb.AppendLine("- Comments and documentation");
        sb.AppendLine("- Non-code content (configs, README, etc.)");
        sb.AppendLine();
        sb.AppendLine("### Subagent Guidelines");
        sb.AppendLine();
        sb.AppendLine("When spawning subagents for code tasks:");
        sb.AppendLine("- Instruct them to use MCPsharp semantic tools");
        sb.AppendLine("- Have them query the codebase rather than reading files");
        sb.AppendLine("- Use structured tool results for decision making");

        return new McpResourceContent
        {
            Uri = "project://guidance",
            MimeType = "text/markdown",
            Text = sb.ToString()
        };
    }

    private static string GetTopLevelDirectory(string filePath, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var firstSep = relativePath.IndexOfAny(['/', '\\']);
        if (firstSep <= 0) return string.Empty;
        return relativePath[..firstSep];
    }
}
