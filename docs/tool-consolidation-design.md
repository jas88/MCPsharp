# MCPsharp Tool Consolidation Design

## Overview

MCPsharp currently has 196 tools organized across multiple services. This design outlines a systematic consolidation to reduce tool count while maintaining functionality, improving discoverability, and optimizing token usage.

## Current Tool Distribution Analysis

### Tool Categories and Count

Based on the McpToolRegistry analysis:

1. **Core File Operations** (8 tools)
   - `project_open`, `project_info`, `file_list`, `file_read`, `file_write`, `file_edit`
   - `validate_file_path`, `get_file_metadata`

2. **Symbol and Code Analysis** (25 tools)
   - `find_symbol`, `get_symbol_info`, `get_class_structure`
   - `find_references`, `find_implementations`, `find_type_usages`
   - Call analysis: `find_callers`, `find_call_chains`, `analyze_call_patterns`

3. **Project and Configuration Analysis** (15 tools)
   - `parse_project`, `get_configuration_schema`, `get_workflows`
   - `validate_workflow_consistency`, `analyze_dependencies`

4. **Architecture and Quality** (30 tools)
   - `validate_architecture`, `detect_layer_violations`
   - `detect_duplicates`, `find_exact_duplicates`, `find_near_duplicates`
   - `analyze_circular_dependencies`, `find_unused_methods`

5. **Bulk Operations** (35 tools)
   - `bulk_replace`, `conditional_edit`, `batch_refactor`
   - `multi_file_edit`, `preview_bulk_changes`

6. **Stream Processing** (40 tools)
   - `stream_process_file`, `bulk_transform`
   - `get_stream_progress`, `list_stream_operations`

7. **Search and Navigation** (20 tools)
   - `search_code_advanced`, `find_files`
   - `trace_feature`, `analyze_impact`

8. **Utility and Helper** (23 tools)
   - `get_available_rollbacks`, `cleanup_stream_operations`
   - `estimate_stream_processing`, `check_temp_directory`

## Consolidation Strategy

### Phase 1: Universal File Operations (8 → 4 tools)

**Consolidate basic file operations into a single universal interface:**

```csharp
// BEFORE (8 tools)
project_open, project_info, file_list, file_read, file_write,
file_edit, validate_file_path, get_file_metadata

// AFTER (4 tools)
file_info, file_content, file_operation, file_batch
```

#### New Universal File Tools

1. **file_info(path, options?)** - Get comprehensive file/directory information
   - Replaces: `project_info`, `file_list`, `get_file_metadata`
   - Returns: Enhanced file info with type, size, structure, dependencies

2. **file_content(path, options?)** - Read and write file content
   - Replaces: `file_read`, `file_write`
   - Supports: Streaming, partial reads, encoding detection

3. **file_operation(path, operations, options?)** - Apply file operations
   - Replaces: `file_edit`, `validate_file_path`
   - Supports: Read-modify-write, batch operations, validation

4. **file_batch(operations, options?)** - Batch file operations
   - NEW: Combines multiple file operations in one call
   - Supports: Cross-file operations, transactions, rollback

### Phase 2: Unified Analysis (70 → 15 tools)

**Consolidate symbol, code, project, and architecture analysis:**

```csharp
// BEFORE (70 tools scattered across 4 categories)
find_symbol, get_symbol_info, get_class_structure,
find_references, find_implementations, parse_project,
validate_architecture, detect_layer_violations, etc.

// AFTER (15 tools organized by scope)
analyze_symbol, analyze_type, analyze_file, analyze_project,
analyze_architecture, analyze_dependencies, analyze_quality
```

#### New Unified Analysis Tools

1. **analyze_symbol(name, context, scope?)** - Symbol-level analysis
   - Replaces: `find_symbol`, `get_symbol_info`, `find_references`
   - Returns: Definition, references, usage patterns, related symbols

2. **analyze_type(type, scope?)** - Type-level analysis
   - Replaces: `get_class_structure`, `find_type_usages`, `find_implementations`
   - Returns: Hierarchy, members, usage, inheritance, patterns

3. **analyze_file(path, analysis_types?)** - File-level analysis
   - Replaces: Multiple file-specific analysis tools
   - Returns: Structure, complexity, metrics, dependencies, issues

4. **analyze_project(path, analysis_types?)** - Project-level analysis
   - Replaces: `parse_project`, project-specific tools
   - Returns: Structure, references, build info, patterns

5. **analyze_architecture(rules?, scope?)** - Architecture analysis
   - Replaces: Architecture validation tools
   - Returns: Compliance, violations, recommendations

6. **analyze_dependencies(scope, depth?)** - Dependency analysis
   - Replaces: Dependency analysis tools
   - Returns: Graph, cycles, impact analysis

7. **analyze_quality(scope, metrics?)** - Code quality analysis
   - Replaces: Duplicate detection, quality tools
   - Returns: Metrics, issues, suggestions, trends

### Phase 3: Bulk Operations Hub (35 → 8 tools)

**Consolidate bulk operations into a unified hub:**

```csharp
// BEFORE (35 tools)
bulk_replace, conditional_edit, batch_refactor,
multi_file_edit, preview_bulk_changes, etc.

// AFTER (8 tools)
bulk_operation, bulk_preview, bulk_progress, bulk_management
```

#### New Bulk Operation Tools

1. **bulk_operation(operations, options?)** - Execute bulk operations
   - Replaces: All bulk edit tools
   - Supports: Replace, refactor, conditional, multi-file operations

2. **bulk_preview(operation, options?)** - Preview bulk changes
   - Replaces: All preview tools
   - Returns: Impact analysis, change list, risk assessment

3. **bulk_progress(operation_id)** - Track bulk operations
   - Replaces: Progress tracking tools
   - Returns: Status, progress, metrics, logs

4. **bulk_management(action, target?)** - Manage bulk operations
   - Replaces: Management tools
   - Supports: Cancel, pause, resume, cleanup, rollback

### Phase 4: Stream Processing Controller (40 → 6 tools)

**Consolidate streaming operations:**

```csharp
// BEFORE (40 tools)
stream_process_file, bulk_transform, get_stream_progress,
list_stream_operations, etc.

// AFTER (6 tools)
stream_process, stream_monitor, stream_management
```

#### New Stream Processing Tools

1. **stream_process(input, processor, options?)** - Process streams
   - Replaces: All stream processing tools
   - Supports: Multiple processors, parallel, compression

2. **stream_monitor(operation_id)** - Monitor stream operations
   - Replaces: Monitoring tools
   - Returns: Progress, metrics, status, logs

3. **stream_management(action, target?)** - Manage stream operations
   - Replaces: Management tools
   - Supports: Start, stop, pause, cleanup

## Implementation Design

### 1. Universal Request/Response Models

```csharp
// Universal request model for all tools
public class ToolRequest
{
    public required string ToolName { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
    public ToolOptions? Options { get; init; }
}

// Universal options model
public class ToolOptions
{
    /// <summary>
    /// Requested detail level (summary, standard, detailed, verbose)
    /// </summary>
    public DetailLevel Detail { get; init; } = DetailLevel.Standard;

    /// <summary>
    /// Include additional data (dependencies, metrics, history, etc.)
    /// </summary>
    public IncludeFlags Include { get; init; } = IncludeFlags.Default;

    /// <summary>
    /// Filters to apply to results
    /// </summary>
    public List<Filter> Filters { get; init; } = new();

    /// <summary>
    /// Sort order for results
    /// </summary>
    public SortSpecification? Sort { get; init; }

    /// <summary>
    /// Pagination for large result sets
    /// </summary>
    public PaginationSpec? Pagination { get; init; }
}

[Flags]
public enum IncludeFlags
{
    Default = 0,
    Metadata = 1 << 0,
    Dependencies = 1 << 1,
    Metrics = 1 << 2,
    History = 1 << 3,
    Suggestions = 1 << 4,
    All = ~0
}
```

### 2. Universal Response Models

```csharp
// Universal response wrapper
public class ToolResponse<T>
{
    public required T Data { get; init; }
    public ResponseMetadata Metadata { get; init; } = new();
    public List<Diagnostic> Diagnostics { get; init; } = new();
    public List<Suggestion> Suggestions { get; init; } = new();
}

// Metadata for all responses
public class ResponseMetadata
{
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ProcessingTime { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
    public int ResultCount { get; init; }
    public bool HasMore { get; init; }
    public string? ContinuationToken { get; init; }
}
```

### 3. Tool Registration Framework

```csharp
// Consolidated tool registry
public class ConsolidatedToolRegistry
{
    private readonly Dictionary<string, ToolHandler> _handlers;

    public ConsolidatedToolRegistry()
    {
        _handlers = RegisterConsolidatedHandlers();
    }

    private Dictionary<string, ToolHandler> RegisterConsolidatedHandlers()
    {
        return new()
        {
            // File Operations (4 tools)
            ["file_info"] = new FileInfoHandler(),
            ["file_content"] = new FileContentHandler(),
            ["file_operation"] = new FileOperationHandler(),
            ["file_batch"] = new FileBatchHandler(),

            // Analysis Tools (15 tools)
            ["analyze_symbol"] = new SymbolAnalysisHandler(),
            ["analyze_type"] = new TypeAnalysisHandler(),
            ["analyze_file"] = new FileAnalysisHandler(),
            ["analyze_project"] = new ProjectAnalysisHandler(),
            ["analyze_architecture"] = new ArchitectureAnalysisHandler(),
            ["analyze_dependencies"] = new DependencyAnalysisHandler(),
            ["analyze_quality"] = new QualityAnalysisHandler(),

            // Bulk Operations (8 tools)
            ["bulk_operation"] = new BulkOperationHandler(),
            ["bulk_preview"] = new BulkPreviewHandler(),
            ["bulk_progress"] = new BulkProgressHandler(),
            ["bulk_management"] = new BulkManagementHandler(),

            // Stream Processing (6 tools)
            ["stream_process"] = new StreamProcessHandler(),
            ["stream_monitor"] = new StreamMonitorHandler(),
            ["stream_management"] = new StreamManagementHandler(),

            // Navigation (5 tools)
            ["search"] = new SearchHandler(),
            ["navigate"] = new NavigationHandler(),
            ["trace"] = new TraceHandler()
        };
    }
}
```

### 4. Handler Base Class

```csharp
// Base class for all tool handlers
public abstract class ToolHandler
{
    protected readonly ILogger _logger;
    protected readonly ResponseProcessor _responseProcessor;

    protected ToolHandler(ILogger logger, ResponseProcessor responseProcessor)
    {
        _logger = logger;
        _responseProcessor = responseProcessor;
    }

    public abstract Task<ToolResponse<object>> HandleAsync(ToolRequest request, CancellationToken ct);

    protected virtual async Task<ToolResponse<T>> CreateResponseAsync<T>(
        T data,
        TimeSpan processingTime,
        List<Diagnostic>? diagnostics = null,
        List<Suggestion>? suggestions = null)
    {
        var response = new ToolResponse<T>
        {
            Data = data,
            Metadata = new ResponseMetadata
            {
                ProcessingTime = processingTime,
                ResultCount = CountResults(data)
            },
            Diagnostics = diagnostics ?? new List<Diagnostic>(),
            Suggestions = suggestions ?? new List<Suggestion>()
        };

        // Apply token optimization
        return await _responseProcessor.ProcessResponseAsync(response);
    }

    private static int CountResults<T>(T data)
    {
        return data switch
        {
            IEnumerable<object> enumerable => enumerable.Count(),
            null => 0,
            _ => 1
        };
    }
}
```

## Migration Strategy

### Phase 1: File Operations (Week 1)

1. **Create new consolidated handlers**
   - Implement FileInfoHandler, FileContentHandler, etc.
   - Add backward compatibility shims

2. **Update tool registry**
   - Register new tools alongside old ones
   - Add deprecation warnings to old tools

3. **Migration path**
   ```csharp
   // Old tool still works but warns
   [Obsolete("Use file_info instead")]
   public async Task<FileListResult> ListFilesAsync(...)
   {
       _logger.LogWarning("file_list is deprecated, use file_info instead");
       return await _fileInfoHandler.HandleAsync(new ToolRequest { ... });
   }
   ```

### Phase 2: Analysis Tools (Week 2-3)

1. **Consolidate analysis services**
   - Create unified AnalysisService
   - Migrate individual analyzers

2. **Implement new handlers**
   - SymbolAnalysisHandler (combines 5 old tools)
   - TypeAnalysisHandler (combines 8 old tools)
   - etc.

3. **Feature mapping**
   ```csharp
   // Map old tool parameters to new ones
   var parameters = new Dictionary<string, object>
   {
       ["target"] = symbolName,
       ["scope"] = "definition",
       ["include"] = IncludeFlags.Dependencies | IncludeFlags.References
   };
   ```

### Phase 3: Bulk Operations (Week 3-4)

1. **Create BulkOperationHub**
   - Consolidate all bulk services
   - Universal operation model

2. **Implement handlers**
   - Single handler for all bulk operations
   - Operation type specified in parameters

### Phase 4: Stream Processing (Week 4-5)

1. **Create StreamController**
   - Unified stream management
   - Processor registry

2. **Consolidate processors**
   - Single interface for all processors
   - Dynamic processor loading

### Phase 5: Cleanup (Week 5-6)

1. **Remove deprecated tools**
   - Remove old handlers
   - Clean up unused models

2. **Optimize**
   - Token optimization
   - Performance tuning
   - Documentation updates

## Benefits of Consolidation

### 1. Reduced Complexity
- From 196 tools to 38 tools (80% reduction)
- Easier to learn and remember
- Clearer organization

### 2. Improved Discoverability
- Logical grouping by functionality
- Consistent parameter patterns
- Better documentation structure

### 3. Token Optimization
- Single tool calls return comprehensive data
- Reduced request/response overhead
- Better pagination support

### 4. Maintainability
- Fewer handlers to maintain
- Consistent error handling
- Unified logging and diagnostics

### 5. Flexibility
- Parameter-driven behavior
- Composable operations
- Extensible architecture

## Example Usage

### Before (Multiple Tool Calls)

```csharp
// 1. Get file info
var fileInfo = await mcp.file_list("src/**/*.cs");

// 2. Read specific file
var content = await mcp.file_read("src/Services/MyService.cs");

// 3. Get class structure
var structure = await mcp.get_class_structure("MyService");

// 4. Find references
var references = await mcp.find_references("MyService");

// 5. Get dependencies
var deps = await mcp.analyze_dependencies("MyService");
```

### After (Single Tool Call)

```csharp
// Single comprehensive call
var analysis = await mcp.analyze_file("src/Services/MyService.cs", new ToolOptions
{
    Detail = DetailLevel.Detailed,
    Include = IncludeFlags.Structure | IncludeFlags.Dependencies | IncludeFlags.References,
    Filters = new List<Filter>
    {
        new Filter { Type = FilterType.Symbol, Value = "MyService" }
    }
});

// Response contains all information in one place
var fileInfo = analysis.Data.FileInfo;
var content = analysis.Data.Content;
var structure = analysis.Data.Structure;
var references = analysis.Data.References;
var dependencies = analysis.Data.Dependencies;
```

## Success Metrics

1. **Tool Count Reduction**: 196 → 38 tools (80% reduction)
2. **Average Response Size**: 30% reduction through optimization
3. **Discovery Time**: 50% faster for users to find right tool
4. **API Calls Reduction**: 60% fewer calls for common workflows
5. **Maintenance Overhead**: 70% reduction in handler code

This consolidation design maintains full functionality while significantly improving the user experience and system maintainability.