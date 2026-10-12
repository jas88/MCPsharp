using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// MCP tool definitions for the Phase 3 architecture validation, duplicate detection,
/// SQL migration and large file optimisation tools.
/// </summary>
public partial class McpToolRegistry
{
    private const string ProjectPathDescription =
        "Project directory or file (relative to the open project root or absolute within it; default: the open project)";
    private const string ArchitectureDefinitionDescription =
        "Architecture definition object (name, description, layers, dependencyRules, namingPatterns, forbiddenPatterns, type), " +
        "e.g. one returned by get_predefined_architectures; property names are case-insensitive";

    /// <summary>
    /// Register the Phase 3 tools whose backing services are available. Architecture, duplicate
    /// detection and large file services are created from the Roslyn workspace when a project opens.
    /// </summary>
    private void RegisterPhase3Tools()
    {
        if (_workspace != null)
        {
            _tools.AddRange(CreateArchitectureTools());
            _tools.AddRange(CreateDuplicateDetectionTools());
        }

        if (_workspace != null || _largeFileOptimizer != null)
        {
            _tools.AddRange(CreateLargeFileTools());
        }

        if (_sqlMigrationAnalyzer != null)
        {
            _tools.AddRange(CreateSqlMigrationTools());
        }
    }

    private static PropertyDefinition ProjectPathProp() => Prop("projectPath", "string", ProjectPathDescription);

    private static PropertyDefinition ArchitectureDefinitionProp(bool required = false) =>
        Prop("definition", "object",
            required ? ArchitectureDefinitionDescription : ArchitectureDefinitionDescription + " (default: best-fit predefined architecture)",
            required: required);

    private static PropertyDefinition DuplicateOptionsProp() =>
        Prop("options", "object",
            "Detection options: minBlockSize, maxBlockSize (lines), similarityThreshold (0-1), ignoreGeneratedCode, ignoreTestCode, " +
            "ignoreTrivialDifferences (booleans), detectionTypes (array of Methods, Classes, CodeBlocks, Files, Properties, Constructors, All)");

    private static PropertyDefinition CodeBlockProp(string name) =>
        Prop(name, "object", "Code block to compare: filePath, startLine, endLine", required: true);

    private static List<McpTool> CreateArchitectureTools() => new()
    {
        new McpTool
        {
            Name = "validate_architecture",
            Description = "Validate the open project's types against an architecture definition: violations, warnings, compliance and per-layer statistics.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), ArchitectureDefinitionProp())
        },
        new McpTool
        {
            Name = "detect_layer_violations",
            Description = "List dependencies that break the architecture's layer rules, with severity counts.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), ArchitectureDefinitionProp())
        },
        new McpTool
        {
            Name = "get_architecture_report",
            Description = "Full architecture report: validation, dependency analysis, diagram and recommendations.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), ArchitectureDefinitionProp())
        },
        new McpTool
        {
            Name = "define_custom_architecture",
            Description = "Register a custom architecture definition for later validation.",
            InputSchema = JsonSchemaHelper.CreateSchema(ArchitectureDefinitionProp(required: true))
        },
        new McpTool
        {
            Name = "analyze_circular_dependencies",
            Description = "Find circular dependencies between types and layers, with severity counts.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), ArchitectureDefinitionProp())
        },
        new McpTool
        {
            Name = "generate_architecture_diagram",
            Description = "Generate a layer and dependency diagram of the open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), ArchitectureDefinitionProp())
        },
        new McpTool
        {
            Name = "get_architecture_recommendations",
            Description = "Recommend fixes for layer violations, with priority, effort and impact.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("violations", "array", "Layer violations as returned by detect_layer_violations or validate_architecture", required: true,
                    items: new Dictionary<string, object> { ["type"] = "object" }))
        },
        new McpTool
        {
            Name = "check_type_compliance",
            Description = "Check one type against an architecture definition: its layer, violations, warnings and dependencies.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("typeName", "string", "Type name (simple or fully qualified)", required: true),
                ArchitectureDefinitionProp(required: true))
        },
        new McpTool
        {
            Name = "get_predefined_architectures",
            Description = "List the predefined architecture definitions, usable as the definition argument of the other architecture tools.",
            InputSchema = JsonSchemaHelper.CreateSchema()
        }
    };

    private static List<McpTool> CreateDuplicateDetectionTools() => new()
    {
        new McpTool
        {
            Name = "detect_duplicates",
            Description = "Detect exact and near-duplicate code in the open project, with metrics, hotspots and refactoring suggestions.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), DuplicateOptionsProp())
        },
        new McpTool
        {
            Name = "find_exact_duplicates",
            Description = "Find code blocks that are identical (after normalisation).",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), DuplicateOptionsProp())
        },
        new McpTool
        {
            Name = "find_near_duplicates",
            Description = "Find code blocks that are similar above a similarity threshold.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("similarityThreshold", "number", "Minimum similarity, 0-1 (default: 0.8)"),
                DuplicateOptionsProp())
        },
        new McpTool
        {
            Name = "analyze_duplication_metrics",
            Description = "Duplication statistics: duplicate groups and lines, percentage, and breakdown by type and file.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), DuplicateOptionsProp())
        },
        new McpTool
        {
            Name = "get_refactoring_suggestions",
            Description = "Suggest refactorings (extract method/class, base class, ...) for duplicate groups.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("duplicates", "array", "Duplicate groups as returned by detect_duplicates (no suggestions are made without them)",
                    items: new Dictionary<string, object> { ["type"] = "object" }),
                Prop("options", "object",
                    "Suggestion options: maxSuggestions (integer), prioritizeBreakingChanges (boolean), refactoringTypes (array of ExtractMethod, ExtractClass, ExtractBaseClass, TemplateMethod, StrategyPattern, UtilityClass, Composition, All)"))
        },
        new McpTool
        {
            Name = "get_duplicate_hotspots",
            Description = "Files and classes with the most duplication.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp(), DuplicateOptionsProp())
        },
        new McpTool
        {
            Name = "compare_code_blocks",
            Description = "Compare two code blocks for similarity.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                CodeBlockProp("codeBlock1"),
                CodeBlockProp("codeBlock2"),
                Prop("options", "object",
                    "Comparison options (booleans): ignoreWhitespace, ignoreComments, ignoreIdentifiers, ignoreStringLiterals, ignoreNumericLiterals"))
        },
        new McpTool
        {
            Name = "validate_refactoring",
            Description = "Check whether a suggested duplicate-removal refactoring is safe to apply.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("suggestion", "object", "Refactoring suggestion as returned by get_refactoring_suggestions", required: true))
        }
    };

    private static List<McpTool> CreateSqlMigrationTools() => new()
    {
        new McpTool
        {
            Name = "analyze_migrations",
            Description = "Analyse Entity Framework migrations: operations, database provider, breaking changes and summary.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp())
        },
        new McpTool
        {
            Name = "detect_breaking_changes",
            Description = "Detect breaking schema changes between two migrations.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("fromMigration", "string", "Earlier migration name", required: true),
                Prop("toMigration", "string", "Later migration name", required: true),
                ProjectPathProp())
        },
        new McpTool
        {
            Name = "get_migration_history",
            Description = "Get the migration history for a DbContext.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("dbContextPath", "string", "DbContext source file (relative to the project root or absolute within it)", required: true))
        },
        new McpTool
        {
            Name = "get_migration_dependencies",
            Description = "Get the migrations a migration depends on and those that depend on it.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("migrationName", "string", "Migration name", required: true),
                ProjectPathProp())
        },
        new McpTool
        {
            Name = "generate_migration_report",
            Description = "Generate a migration report: statistics, risk assessment and recommendations.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("includeHistory", "boolean", "Include migration history (default: true)"))
        },
        new McpTool
        {
            Name = "validate_migrations",
            Description = "Validate the project's migrations and report issues.",
            InputSchema = JsonSchemaHelper.CreateSchema(ProjectPathProp())
        }
    };

    private static PropertyDefinition RequiredFilePathProp() =>
        Prop("filePath", "string", "C# file (relative to the project root or absolute within it)", required: true);

    private static List<McpTool> CreateLargeFileTools() => new()
    {
        new McpTool
        {
            Name = "analyze_large_files",
            Description = "Find large files, classes and methods in the open project with size and complexity statistics.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("maxLines", "integer", "Line count above which a file counts as large (default: 500)"))
        },
        new McpTool
        {
            Name = "optimize_large_class",
            Description = "Suggest how to split the large classes in a file (responsibilities, extraction candidates).",
            InputSchema = JsonSchemaHelper.CreateSchema(RequiredFilePathProp())
        },
        new McpTool
        {
            Name = "optimize_large_method",
            Description = "Suggest how to break up a large method (extraction candidates, complexity reduction).",
            InputSchema = JsonSchemaHelper.CreateSchema(
                RequiredFilePathProp(),
                Prop("methodName", "string", "Method name", required: true))
        },
        new McpTool
        {
            Name = "get_complexity_metrics",
            Description = "Complexity metrics (cyclomatic, cognitive, nesting, size) for a file or one method in it.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                RequiredFilePathProp(),
                Prop("methodName", "string", "Limit the metrics to this method"))
        },
        new McpTool
        {
            Name = "generate_optimization_plan",
            Description = "Prioritised optimisation plan for a file: actions, effort, benefit and risk.",
            InputSchema = JsonSchemaHelper.CreateSchema(RequiredFilePathProp())
        },
        new McpTool
        {
            Name = "detect_god_classes",
            Description = "Detect god classes (too many responsibilities, members or dependencies).",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("filePath", "string", "Limit detection to this file"))
        },
        new McpTool
        {
            Name = "detect_god_methods",
            Description = "Detect god methods (too long, complex or with too many parameters).",
            InputSchema = JsonSchemaHelper.CreateSchema(
                ProjectPathProp(),
                Prop("filePath", "string", "Limit detection to this file"))
        },
        new McpTool
        {
            Name = "analyze_code_smells",
            Description = "Detect code smells in a file (long methods, large classes, long parameter lists, feature envy, ...).",
            InputSchema = JsonSchemaHelper.CreateSchema(RequiredFilePathProp())
        },
        new McpTool
        {
            Name = "get_optimization_recommendations",
            Description = "Code smells in a file together with suggested refactoring patterns.",
            InputSchema = JsonSchemaHelper.CreateSchema(RequiredFilePathProp())
        }
    };
}
