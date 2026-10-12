using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// MCP tool definitions for the bulk edit, streaming processor and Roslyn code fix/configuration tools.
/// </summary>
public partial class McpToolRegistry
{
    private const string FilesDescription =
        "Files, directories or glob patterns (e.g. src/**/*.cs), relative to the project root or absolute within it";

    private static readonly string[] BulkConditionTypeValues =
        { "FileContains", "FileMatches", "FileSize", "FileModifiedAfter", "FileExtension", "FileInDirectory" };
    private static readonly string[] BulkRefactorTypeValues =
    {
        "RenameSymbol", "ChangeMethodSignature", "ExtractMethod", "InlineMethod", "MoveNamespace",
        "AddUsing", "RemoveUnusedUsings", "FormatCode", "ApplyCodeFix"
    };
    private static readonly string[] BulkEditOperationTypeValues =
        { "BulkReplace", "ConditionalEdit", "BatchRefactor", "MultiFileEdit", "PreviewChanges", "RollbackOperation" };
    private static readonly string[] PreviewOperationTypeValues = { "BulkReplace", "ConditionalEdit", "MultiFileEdit" };
    private static readonly string[] ValidateOperationTypeValues = { "BulkReplace", "ConditionalEdit" };
    private static readonly string[] FileEditTypeValues = { "insert", "delete", "replace" };
    private static readonly string[] FileEditRequiredProperties = { "type" };
    private static readonly string[] MultiFileOperationRequiredProperties = { "filePattern", "edits" };
    private static readonly string[] StreamProcessorTypeValues =
        { "LineProcessor", "RegexProcessor", "CsvProcessor", "BinaryProcessor", "JsonProcessor", "CustomProcessor" };
    private static readonly string[] AnalyzerSeverityValues = { "Info", "Warning", "Error", "Critical" };
    private static readonly string[] ReportFormatValues = { "html", "markdown", "json", "csv" };

    private static readonly Dictionary<string, object> IntegerSchema = new() { ["type"] = "integer" };

    /// <summary>
    /// Register the bulk edit, streaming and code fix tools whose backing services were supplied.
    /// </summary>
    private void RegisterBulkStreamAndCodeFixTools()
    {
        if (_bulkEditService != null)
        {
            _tools.AddRange(CreateBulkEditTools());
        }

        if (_streamingProcessor != null)
        {
            _tools.AddRange(CreateStreamProcessorTools());
        }

        if (_progressTracker != null)
        {
            _tools.AddRange(CreateStreamProgressTools());
        }

        if (_tempFileManager != null)
        {
            _tools.Add(new McpTool
            {
                Name = "cleanup_stream_operations",
                Description = "Delete temporary streaming files older than the given age.",
                InputSchema = JsonSchemaHelper.CreateSchema(
                    Prop("olderThanHours", "integer", "Remove temporary files older than this many hours (default: 24)"))
            });
        }

        if (_roslynAnalyzerService != null)
        {
            _tools.AddRange(CreateRoslynCodeFixTools());
        }

        _tools.AddRange(CreateAnalyzerConfigurationTools());
    }

    private static Dictionary<string, object> FileEditItems() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["type"] = EnumItems(FileEditTypeValues),
            ["newText"] = new Dictionary<string, object> { ["type"] = "string" },
            ["startLine"] = IntegerSchema,
            ["startColumn"] = IntegerSchema,
            ["endLine"] = IntegerSchema,
            ["endColumn"] = IntegerSchema
        },
        ["required"] = FileEditRequiredProperties
    };

    private static PropertyDefinition FileEditsProp(string description, bool required = false) =>
        Prop("edits", "array", description + " (0-indexed lines/columns; insert uses startLine/startColumn)",
            required: required, items: FileEditItems());

    private static PropertyDefinition MultiFileOperationsProp(string description, bool required = false) =>
        Prop("operations", "array", description, required: required, items: new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["filePattern"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = FilesDescription },
                ["edits"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = FileEditItems() },
                ["priority"] = IntegerSchema,
                ["dependsOn"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = StringItems }
            },
            ["required"] = MultiFileOperationRequiredProperties
        });

    private static PropertyDefinition BulkFilesProp() =>
        Prop("files", "array", FilesDescription, required: true, items: StringItems);

    private static List<McpTool> CreateBulkEditTools() => new()
    {
        new McpTool
        {
            Name = "bulk_replace",
            Description = "Regex find-and-replace across many files in the open project, with optional preview and backups for rollback.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("files", "array", "Files to edit, relative to the project root or absolute within it (patterns are not expanded; missing files are reported as failures)",
                    required: true, items: StringItems),
                Prop("regexPattern", "string", "Regular expression to match", required: true),
                Prop("replacement", "string", "Replacement text (supports $1-style group references)", required: true),
                Prop("maxParallelism", "integer", "Maximum files processed in parallel (default: processor count)"),
                Prop("createBackups", "boolean", "Back up files so the edit can be rolled back (default: true)"),
                Prop("previewMode", "boolean", "Compute changes without writing (default: false)"),
                Prop("excludedFiles", "array", "Exclusion glob patterns; only applied when files are expanded from patterns, so they have no effect on the explicit files of bulk_replace", items: StringItems))
        },
        new McpTool
        {
            Name = "conditional_edit",
            Description = "Apply text edits to every file in the open project that satisfies a condition.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                BulkFilesProp(),
                Prop("conditionType", "string", "Condition to evaluate per file", required: true, enumValues: BulkConditionTypeValues),
                Prop("pattern", "string", "Condition value: text (FileContains, case-sensitive), regex (FileMatches), extension such as .cs (FileExtension) or directory (FileInDirectory)", required: true),
                Prop("negate", "boolean", "Apply the edits to files NOT meeting the condition (default: false)"),
                FileEditsProp("Edits applied to each matching file", required: true))
        },
        new McpTool
        {
            Name = "batch_refactor",
            Description = "Apply a pattern-based refactoring across many files in the open project.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                BulkFilesProp(),
                Prop("refactorType", "string", "Refactoring to apply", required: true, enumValues: BulkRefactorTypeValues),
                Prop("targetPattern", "string", "Pattern identifying the code to refactor", required: true),
                Prop("replacementPattern", "string", "Replacement pattern", required: true))
        },
        new McpTool
        {
            Name = "multi_file_edit",
            Description = "Apply different text edits to different files (or file patterns) in one coordinated operation, ordered by priority.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                MultiFileOperationsProp("Per-file edit operations", required: true))
        },
        new McpTool
        {
            Name = "preview_bulk_changes",
            Description = "Preview the changes a bulk replace, conditional edit or multi-file edit would make, without writing.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationType", "string", "Operation to preview", required: true, enumValues: PreviewOperationTypeValues),
                BulkFilesProp(),
                Prop("regexPattern", "string", "BulkReplace: regular expression (required for BulkReplace)"),
                Prop("replacement", "string", "BulkReplace: replacement text (required for BulkReplace)"),
                Prop("conditionType", "string", "ConditionalEdit: condition type (required for ConditionalEdit)", enumValues: BulkConditionTypeValues),
                Prop("pattern", "string", "ConditionalEdit: condition value (required for ConditionalEdit)"),
                MultiFileOperationsProp("MultiFileEdit: per-file edit operations (required for MultiFileEdit)"))
        },
        new McpTool
        {
            Name = "rollback_bulk_edit",
            Description = "Restore the files changed by a bulk edit that was run with backups.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("rollbackId", "string", "Rollback ID from the bulk edit result or get_available_rollbacks", required: true))
        },
        new McpTool
        {
            Name = "validate_bulk_edit",
            Description = "Validate a bulk replace or conditional edit before running it: pattern validity, matching files and potential issues.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationType", "string", "Operation to validate", required: true, enumValues: ValidateOperationTypeValues),
                BulkFilesProp(),
                Prop("regexPattern", "string", "BulkReplace: regular expression (required for BulkReplace)"),
                Prop("conditionType", "string", "ConditionalEdit: condition type (required for ConditionalEdit)", enumValues: BulkConditionTypeValues),
                Prop("pattern", "string", "ConditionalEdit: condition value (required for ConditionalEdit)"))
        },
        new McpTool
        {
            Name = "get_available_rollbacks",
            Description = "List bulk edit sessions that can be rolled back.",
            InputSchema = JsonSchemaHelper.CreateSchema()
        },
        new McpTool
        {
            Name = "estimate_bulk_impact",
            Description = "Estimate the scope, risk and duration of a bulk edit before running it.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationType", "string", "Kind of bulk operation", required: true, enumValues: BulkEditOperationTypeValues),
                BulkFilesProp())
        },
        new McpTool
        {
            Name = "get_bulk_file_statistics",
            Description = "Summarise the files a bulk operation would touch: counts, sizes and file types.",
            InputSchema = JsonSchemaHelper.CreateSchema(BulkFilesProp())
        }
    };

    private static PropertyDefinition StreamProcessorTypeProp() =>
        Prop("processorType", "string", "Processor to apply (case-insensitive)", required: true, enumValues: StreamProcessorTypeValues);

    private static PropertyDefinition ProcessorOptionsProp() =>
        Prop("processorOptions", "object", "Processor-specific options (e.g. pattern/replacement for RegexProcessor)");

    private static List<McpTool> CreateStreamProcessorTools() => new()
    {
        new McpTool
        {
            Name = "stream_process_file",
            Description = "Process a large file in the open project in chunks with a streaming processor, writing the result to an output file. Returns an operation ID for progress tracking.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("filePath", "string", "Input file (relative to the project root or absolute within it)", required: true),
                Prop("outputPath", "string", "Output file (relative to the project root or absolute within it)", required: true),
                StreamProcessorTypeProp(),
                Prop("chunkSize", "integer", "Chunk size in bytes (default: 65536)"),
                Prop("createCheckpoint", "boolean", "Create checkpoints so the operation can be resumed (default: true)"),
                Prop("enableCompression", "boolean", "Compress the output (default: false)"),
                ProcessorOptionsProp())
        },
        new McpTool
        {
            Name = "bulk_transform",
            Description = "Stream-process many files in the open project in parallel, writing results to an output directory.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("inputFiles", "array", "Input files or directories (a single string is also accepted), relative to the project root or absolute within it", required: true, items: StringItems),
                Prop("outputDirectory", "string", "Output directory (relative to the project root or absolute within it)", required: true),
                StreamProcessorTypeProp(),
                Prop("chunkSize", "integer", "Chunk size in bytes (default: 65536)"),
                Prop("maxDegreeOfParallelism", "integer", "Maximum files processed in parallel (default: 4)"),
                Prop("preserveDirectoryStructure", "boolean", "Mirror input directory structure in the output (default: true)"),
                Prop("filePattern", "string", "File search pattern for directory inputs (default: *)"),
                Prop("recursive", "boolean", "Include subdirectories of directory inputs (default: false)"),
                ProcessorOptionsProp())
        },
        new McpTool
        {
            Name = "cancel_stream_operation",
            Description = "Cancel a running stream operation.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationId", "string", "Operation ID returned by stream_process_file or bulk_transform", required: true))
        },
        new McpTool
        {
            Name = "resume_stream_operation",
            Description = "Resume an interrupted stream operation from its last checkpoint.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationId", "string", "Operation ID of the interrupted operation", required: true))
        },
        new McpTool
        {
            Name = "get_available_processors",
            Description = "List the available streaming processors and their capabilities.",
            InputSchema = JsonSchemaHelper.CreateSchema()
        },
        new McpTool
        {
            Name = "estimate_stream_processing",
            Description = "Estimate time, chunk count and resource usage for stream-processing a file.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("filePath", "string", "Input file (relative to the project root or absolute within it)", required: true),
                StreamProcessorTypeProp(),
                Prop("chunkSize", "integer", "Chunk size in bytes (default: 65536)"),
                ProcessorOptionsProp())
        }
    };

    private static List<McpTool> CreateStreamProgressTools() => new()
    {
        new McpTool
        {
            Name = "get_stream_progress",
            Description = "Get progress for a stream operation.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("operationId", "string", "Operation ID returned by stream_process_file or bulk_transform", required: true))
        },
        new McpTool
        {
            Name = "list_stream_operations",
            Description = "List tracked stream operations and their progress.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("maxCount", "integer", "Maximum operations to return (default: 50)"))
        }
    };

    private static List<McpTool> CreateRoslynCodeFixTools() => new()
    {
        new McpTool
        {
            Name = "apply_roslyn_fixes",
            Description = "Apply the code fixes from loaded Roslyn code fix providers for the given diagnostics in a file. Previews (diff, no write) by default; when applied, files are backed up and the returned rollbackId can be passed to rollback_bulk_edit.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("diagnostic_ids", "array", "Diagnostic IDs to fix (e.g. CA1822)", required: true, items: StringItems),
                Prop("target_path", "string", "File to fix (relative to the project root or absolute within it)", required: true),
                Prop("preview", "boolean", "Return the fixes without writing them (default: true)"))
        },
        new McpTool
        {
            Name = "generate_analysis_report",
            Description = "Write a report of the most recent run_roslyn_analyzers results.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("format", "string", "Report format", required: true, enumValues: ReportFormatValues),
                Prop("output_path", "string", "Report file to write (relative to the project root or absolute within it)", required: true))
        }
    };

    private static List<McpTool> CreateAnalyzerConfigurationTools() => new()
    {
        new McpTool
        {
            Name = "configure_analyzer",
            Description = "Enable or disable an analyzer, or set the severity of one of its rules. Provide 'enabled', or 'rule_id' and 'severity'.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("analyzer_id", "string", "Analyzer ID", required: true),
                Prop("enabled", "boolean", "Enable or disable the analyzer"),
                Prop("rule_id", "string", "Rule ID whose severity to set"),
                Prop("severity", "string", "Severity for rule_id (case-insensitive)", enumValues: AnalyzerSeverityValues))
        },
        new McpTool
        {
            Name = "get_analyzer_config",
            Description = "Get the current analyzer configuration (enabled analyzers and rule severities).",
            InputSchema = JsonSchemaHelper.CreateSchema()
        },
        new McpTool
        {
            Name = "reset_analyzer_config",
            Description = "Reset the analyzer configuration to defaults.",
            InputSchema = JsonSchemaHelper.CreateSchema()
        }
    };
}
