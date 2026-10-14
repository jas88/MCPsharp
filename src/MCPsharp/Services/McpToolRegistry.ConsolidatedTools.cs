using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// MCP tool definitions for the consolidated services (universal file operations,
/// unified analysis, bulk operations hub and stream processing controller).
/// </summary>
public partial class McpToolRegistry
{
    private static readonly string[] DetailLevelValues = { "basic", "standard", "detailed", "verbose" };
    private static readonly string[] IncludeOptionValues =
        { "metadata", "dependencies", "metrics", "history", "suggestions", "preview", "new_content", "all" };
    private static readonly string[] FileContentOperationValues = { "read", "write", "append", "partial_read" };
    private static readonly string[] FileOperationTypeValues = { "edit", "validate", "copy", "move", "delete", "create" };
    private static readonly string[] AnalysisScopeValues = { "definition", "references", "dependencies", "all" };
    private static readonly string[] FileAnalysisTypeValues = { "structure", "complexity", "dependencies", "quality", "all" };
    private static readonly string[] ProjectAnalysisTypeValues = { "structure", "dependencies", "build", "metrics", "all" };
    private static readonly string[] QualityMetricValues =
        { "duplicates", "complexity", "maintainability", "test_coverage", "code_smells", "all" };
    private static readonly string[] BulkOperationTypeValues = { "replace", "conditional", "refactor", "multi_edit", "file_operation" };
    private static readonly string[] BulkManagementActionValues = { "list", "cancel", "pause", "resume", "cleanup", "status" };
    private static readonly string[] StreamInputTypeValues = { "file", "directory", "multiple_files" };
    private static readonly string[] StreamProcessorValues = { "line", "regex", "csv", "json", "binary", "xml", "log" };

    private const string ProcessorOptionsDescription =
        "Processor parameters (keys are case-insensitive). " +
        "line: LineFilter, LineReplacement, LinePrefix, LineSuffix, IncludeLineNumbers, RemoveEmptyLines, TrimWhitespace, FieldSeparator, SelectedFields, Encoding. " +
        "regex: Pattern, Replacement, IgnoreCase, Multiline, MatchWholeLine, MaxMatches (per chunk; 0 = unlimited), Options (raw System.Text.RegularExpressions.RegexOptions flags as an integer). " +
        "csv: Delimiter, QuoteCharacter, SelectedColumnIndexes, SkipEmptyRows, TrimFields. " +
        "json: JsonLines, Indented. xml: Indent. log: MinLevel, Levels, Pattern, IgnoreCase. " +
        "binary: SearchPattern and ReplacementPattern (supplied together; a UTF-8 string or an array of byte values 0-255), CalculateChecksums, ChecksumAlgorithm (SHA256, SHA512).";
    private static readonly string[] StreamManagementActionValues = { "list", "cancel", "pause", "resume", "cleanup", "unregister_processor" };

    private static readonly string[] FileOperationRequiredProperties = { "path", "operation" };
    private static readonly string[] EditConditionRequiredProperties = { "type", "value" };

    private static readonly Dictionary<string, object> StringItems = new() { ["type"] = "string" };

    /// <summary>
    /// Register the consolidated tools whose backing service was supplied to the registry.
    /// </summary>
    private void RegisterConsolidatedTools()
    {
        if (_universalFileOps != null)
        {
            _tools.AddRange(CreateFileOperationTools());
        }

        if (_unifiedAnalysis != null)
        {
            _tools.AddRange(CreateUnifiedAnalysisTools());
        }

        if (_bulkOperationsHub != null)
        {
            _tools.AddRange(CreateBulkOperationTools());
        }

        if (_streamController != null)
        {
            _tools.AddRange(CreateStreamProcessingTools());
        }
    }

    private static PropertyDefinition Prop(string name, string type, string description,
        bool required = false, string[]? enumValues = null, object? items = null) => new()
        {
            Name = name,
            Type = type,
            Description = description,
            Required = required,
            Enum = enumValues,
            Items = items
        };

    private static Dictionary<string, object> EnumItems(string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = values
    };

    private static PropertyDefinition DetailProp() =>
        Prop("detail", "string", "Response detail level (default: standard)", enumValues: DetailLevelValues);

    private static PropertyDefinition IncludeProp() =>
        Prop("include", "array", "Additional data to include in the response", items: EnumItems(IncludeOptionValues));

    private static PropertyDefinition EditsProp(
        string description = "Text edits for 'edit' operations, same shape as file_edit edits (0-indexed lines/columns)") =>
        Prop("edits", "array", description,
            items: new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["type"] = EnumItems(EditOperationTypes),
                    ["start_line"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["start_column"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["end_line"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["end_column"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["new_text"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["line"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["column"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["text"] = new Dictionary<string, object> { ["type"] = "string" }
                },
                ["required"] = EditRequiredProperties
            });

    private static readonly string[] EditConditionTypeValues =
        { "contains", "notcontains", "regex", "notregex", "extension", "directory" };
    private static readonly string[] EditConditionOperatorValues = { "not" };

    private static PropertyDefinition ConditionsProp() =>
        Prop("conditions", "array",
            "conditional: conditions every file must satisfy before the edits are applied. contains/notcontains: case-sensitive text; regex/notregex: regular expression; extension: file extension (e.g. .cs); directory: containing directory. operator \"not\" negates a condition.",
            items: new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["type"] = EnumItems(EditConditionTypeValues),
                    ["value"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["operator"] = EnumItems(EditConditionOperatorValues)
                },
                ["required"] = EditConditionRequiredProperties
            });

    private static PropertyDefinition BulkEditsProp() =>
        EditsProp("conditional: text edits applied to every file meeting the conditions, same shape as file_edit edits (0-indexed lines/columns)");

    private static Dictionary<string, object> FileOperationItems() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["path"] = new Dictionary<string, object> { ["type"] = "string" },
            ["operation"] = EnumItems(FileOperationTypeValues),
            ["destination_path"] = new Dictionary<string, object> { ["type"] = "string" },
            ["edits"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "object" } },
            ["overwrite"] = new Dictionary<string, object> { ["type"] = "boolean" },
            ["dry_run"] = new Dictionary<string, object> { ["type"] = "boolean" }
        },
        ["required"] = FileOperationRequiredProperties
    };

    private static List<McpTool> CreateFileOperationTools() => new()
    {
        new McpTool
        {
            Name = "get_file_info",
            Description = "Get information about a file or directory in the open project (type, size, language, category, line count; directory listings include children).",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("path", "string", "File or directory path (relative to project root or absolute within it)", required: true),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "get_file_content",
            Description = "Read, partially read, write or append file content in the open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("path", "string", "File path (relative to project root or absolute within it)", required: true),
                Prop("operation", "string", "Content operation (default: read)", enumValues: FileContentOperationValues),
                Prop("content", "string", "Content to write or append"),
                Prop("start_line", "integer", "First line for partial_read (1-based)"),
                Prop("end_line", "integer", "Last line for partial_read (1-based)"),
                Prop("create_directories", "boolean", "Create missing directories when writing (default: true)"),
                Prop("overwrite", "boolean", "Overwrite an existing file when writing (default: false)"))
        },
        new McpTool
        {
            Name = "execute_file_operation",
            Description = "Apply a single file operation (edit, validate, copy, move, delete, create) within the open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("path", "string", "Source file path", required: true),
                Prop("operation", "string", "Operation to perform", required: true, enumValues: FileOperationTypeValues),
                Prop("destination_path", "string", "Destination path for copy/move"),
                EditsProp(),
                Prop("overwrite", "boolean", "Overwrite destination for copy/move (default: false)"),
                Prop("create_backup", "boolean", "Create a backup before modifying (default: false)"),
                Prop("dry_run", "boolean", "Validate without changing anything (default: false)"),
                Prop("include_new_content", "boolean", "Return the new file content after an edit (default: false)"),
                Prop("require_utf8", "boolean", "Validation: require UTF-8 encoding (default: false)"),
                Prop("max_file_size", "integer", "Validation: maximum file size in bytes"))
        },
        new McpTool
        {
            Name = "execute_batch",
            Description = "Execute several file operations in one call, optionally as a transaction with rollback.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operations", "array", "File operations to execute in order", required: true, items: FileOperationItems()),
                Prop("enable_transactions", "boolean", "Roll back all operations if one fails (default: false)"),
                Prop("fail_fast", "boolean", "Stop at the first failure (default: true)"),
                Prop("continue_on_error", "boolean", "Continue executing after a failure (default: false)"))
        }
    };

    private static List<McpTool> CreateUnifiedAnalysisTools() => new()
    {
        new McpTool
        {
            Name = "analyze_symbol",
            Description = "Unified symbol analysis: definition, references, callers, usage patterns and metrics in one call. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("symbol_name", "string", "Symbol name to analyze", required: true),
                Prop("context", "string", "Optional context to disambiguate the symbol (e.g. containing type or file)"),
                Prop("scope", "string", "Analysis scope (default: all)", enumValues: AnalysisScopeValues),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_type",
            Description = "Unified type analysis: structure, members, hierarchy, usages and implementations. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("type_name", "string", "Type name to analyze", required: true),
                Prop("context", "string", "Optional context (namespace or file) to disambiguate the type"),
                Prop("scope", "string", "Analysis scope (default: all)", enumValues: AnalysisScopeValues),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_file",
            Description = "Unified file analysis: structure, complexity, dependencies and quality of a single file. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("file_path", "string", "File path (relative to project root or absolute within it)", required: true),
                Prop("analysis_types", "array", "Analyses to run (default: all)", items: EnumItems(FileAnalysisTypeValues)),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_project",
            Description = "Unified project analysis: structure, file inventory, dependencies, build and metrics. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("project_path", "string", "Project directory or file (default: the open project root)"),
                Prop("analysis_types", "array", "Analyses to run (default: all)", items: EnumItems(ProjectAnalysisTypeValues)),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_architecture",
            Description = "Unified architecture analysis of the open project against the default layered architecture: validation, layer violations, layer dependencies and recommendations. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("scope", "string", "Sections to compute: definition = validation against the architecture, references = layer violations, dependencies = layer dependency graph, all = everything (default: all)", enumValues: AnalysisScopeValues),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_dependencies",
            Description = "Unified dependency analysis: dependency graph, circular dependencies and impact. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("scope", "string", "Analysis scope (default: dependencies)", enumValues: AnalysisScopeValues),
                Prop("type_name", "string", "Optional type to focus the analysis on"),
                Prop("depth", "integer", "Maximum dependency depth (default: 2)"),
                Prop("include_circular", "boolean", "Detect circular dependencies (default: true)"),
                DetailProp(),
                IncludeProp())
        },
        new McpTool
        {
            Name = "analyze_quality",
            Description = "Unified code quality analysis of the open project (or one file in it): duplicates, complexity, maintainability, test coverage and code smells. Requires an open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("scope", "string", "Label echoed in the response; use metrics to choose the analyses (default: all)", enumValues: AnalysisScopeValues),
                Prop("metrics", "array", "Quality metrics to compute (default: all)", items: EnumItems(QualityMetricValues)),
                Prop("file_path", "string", "Optional file to analyse instead of the whole project (duplicates are detected project-wide and filtered to this file)"),
                DetailProp(),
                IncludeProp())
        }
    };

    private static List<McpTool> CreateBulkOperationTools() => new()
    {
        new McpTool
        {
            Name = "execute_bulk_operation",
            Description = "Execute a bulk operation across many files in the open project: regex replace, conditional match, refactor, multi-file edit or file operations. Returns an operation_id usable with get_bulk_progress.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operation_type", "string", "Bulk operation type", required: true, enumValues: BulkOperationTypeValues),
                Prop("files", "array", "Files to process (relative to project root or absolute within it)", items: StringItems),
                Prop("pattern", "string", "replace: regex pattern"),
                Prop("replacement", "string", "replace: replacement text"),
                ConditionsProp(),
                BulkEditsProp(),
                Prop("refactor_type", "string", "refactor: refactoring type name (e.g. RenameSymbol)"),
                Prop("target_pattern", "string", "refactor: pattern to match"),
                Prop("replacement_pattern", "string", "refactor: replacement pattern"),
                Prop("operations", "array", "multi_edit / file_operation: per-file operations", items: FileOperationItems()),
                Prop("dry_run", "boolean", "Compute changes without writing (default: false)"),
                Prop("create_backup", "boolean", "Back up files and roll back on failure (default: false)"),
                Prop("max_parallelism", "integer", "Maximum files processed in parallel (default: 10)"),
                Prop("fail_fast", "boolean", "Stop at the first failure (default: true)"))
        },
        new McpTool
        {
            Name = "preview_bulk_operation",
            Description = "Preview a bulk operation without writing: per-file results, impact analysis, risk assessment and time estimate. Accepts the same operation arguments as execute_bulk_operation.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operation_type", "string", "Bulk operation type", required: true, enumValues: BulkOperationTypeValues),
                Prop("files", "array", "Files to process (relative to project root or absolute within it)", items: StringItems),
                Prop("pattern", "string", "replace: regex pattern"),
                Prop("replacement", "string", "replace: replacement text"),
                ConditionsProp(),
                BulkEditsProp(),
                Prop("refactor_type", "string", "refactor: refactoring type name (e.g. RenameSymbol)"),
                Prop("target_pattern", "string", "refactor: pattern to match"),
                Prop("replacement_pattern", "string", "refactor: replacement pattern"),
                Prop("operations", "array", "multi_edit / file_operation: per-file operations", items: FileOperationItems()),
                Prop("max_preview_files", "integer", "Maximum number of files to include in the preview"))
        },
        new McpTool
        {
            Name = "get_bulk_progress",
            Description = "Get status, progress and recent log entries for a bulk operation started with execute_bulk_operation.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operation_id", "string", "Operation ID returned by execute_bulk_operation", required: true))
        },
        new McpTool
        {
            Name = "manage_bulk_operation",
            Description = "Manage bulk operations: list active operations, cancel/pause/resume/status of one operation, or clean up old ones.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("action", "string", "Management action", required: true, enumValues: BulkManagementActionValues),
                Prop("operation_id", "string", "Target operation ID (required for cancel, pause, resume and status)"),
                Prop("max_age_hours", "number", "cleanup: remove operations older than this many hours"))
        }
    };

    private static List<McpTool> CreateStreamProcessingTools() => new()
    {
        new McpTool
        {
            Name = "process_stream",
            Description = "Stream-process large files in the open project with a built-in processor (line, regex, csv, json, binary, xml, log) in chunks; json loads the whole document into memory unless JsonLines=true, which processes it line by line. Returns an operation_id usable with monitor_stream.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("processor_type", "string", "Processor to apply", required: true, enumValues: StreamProcessorValues),
                Prop("input_type", "string", "Input kind (default: file)", enumValues: StreamInputTypeValues),
                Prop("input_path", "string", "Input file or directory (required for file and directory input)"),
                Prop("files", "array", "multiple_files: files to process", items: StringItems),
                Prop("output_path", "string", "Output file (file input) or directory (directory / multiple_files input)"),
                Prop("search_pattern", "string", "directory: file search pattern (default: *)"),
                Prop("recursive", "boolean", "directory: include subdirectories (default: false)"),
                Prop("chunk_size", "integer", "Chunk size in bytes (default: 65536)"),
                Prop("enable_compression", "boolean", "Compress output (default: false)"),
                Prop("enable_checkpoints", "boolean", "Enable checkpoints for resumable processing (default: false)"),
                Prop("processor_options", "object", ProcessorOptionsDescription))
        },
        new McpTool
        {
            Name = "monitor_stream",
            Description = "Get status, progress, performance and logs for a stream operation started with process_stream.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operation_id", "string", "Operation ID returned by process_stream", required: true),
                Prop("include_details", "boolean", "Include detailed progress (default: true)"),
                Prop("include_logs", "boolean", "Include recent log entries (default: true)"),
                Prop("include_performance", "boolean", "Include performance metrics (default: true)"),
                Prop("max_log_entries", "integer", "Maximum log entries to return"))
        },
        new McpTool
        {
            Name = "manage_stream",
            Description = "Manage stream operations: list active streams, cancel/pause/resume one, clean up old ones, or unregister a processor.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("action", "string", "Management action", required: true, enumValues: StreamManagementActionValues),
                Prop("operation_id", "string", "Target operation ID (cancel, pause, resume) or processor name (unregister_processor)"),
                Prop("max_age_hours", "number", "cleanup: remove streams older than this many hours"))
        }
    };
}
