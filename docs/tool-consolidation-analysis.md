# MCPsharp Tool Consolidation Analysis

## Executive Summary
MCPsharp currently has **119 unique MCP tools**, significantly above the typical 20-40 tools in comparable MCP servers. This analysis proposes consolidation strategies to reduce tool count by 40-50% while improving usability and maintaining all functionality.

## Current State: 119 Tools Inventory

### Tool Categories and Counts

#### 1. File Operations (5 tools)
- `project_open` - Open project directory
- `project_info` - Get project information
- `file_list` - List project files
- `file_read` - Read file contents
- `file_write` - Write file contents
- `file_edit` - Apply text edits

#### 2. Symbol & Reference Finding (15 tools)
- `find_symbol` - Find symbols by name
- `find_references` - Find symbol references
- `find_implementations` - Find interface implementations
- `find_callers` - Find method callers
- `find_call_chains` - Find call chains
- `find_type_usages` - Find type usages
- `find_circular_dependencies` - Find circular deps
- `find_unused_methods` - Find unused methods
- `find_recursive_calls` - Find recursive calls
- `find_exact_duplicates` - Find exact code duplicates
- `find_near_duplicates` - Find similar code blocks
- `get_symbol_info` - Get symbol details
- `get_class_structure` - Get class structure
- `search_text` - Text search (appears to be redundant)

#### 3. Analysis Tools (30+ tools)
- `analyze_impact` - Impact analysis
- `analyze_call_graph` - Call graph analysis
- `analyze_call_patterns` - Call pattern analysis
- `analyze_inheritance` - Inheritance analysis
- `analyze_type_dependencies` - Type dependency analysis
- `analyze_architecture` - Architecture validation
- `analyze_circular_dependencies` - Circular dependency analysis
- `analyze_code_smells` - Code smell detection
- `analyze_dependencies` - General dependency analysis
- `analyze_duplication_metrics` - Duplication metrics
- `analyze_file` - Single file analysis
- `analyze_large_files` - Large file analysis
- `analyze_migrations` - Migration analysis
- `analyze_project` - Project-wide analysis
- `analyze_quality` - Quality metrics
- `analyze_symbol` - Symbol-specific analysis
- `analyze_type` - Type analysis

#### 4. Code Editing & Refactoring (10 tools)
- `add_class_property` - Add property to class
- `add_class_method` - Add method to class
- `optimize_large_class` - Optimize large classes
- `optimize_large_method` - Optimize large methods
- `batch_refactor` - Batch refactoring
- `validate_refactoring` - Validate refactorings

#### 5. Bulk Operations (11 tools)
- `bulk_replace` - Bulk text replacement
- `bulk_transform` - Bulk file transformation
- `conditional_edit` - Conditional editing
- `multi_file_edit` - Multi-file edits
- `preview_bulk_changes` - Preview changes
- `rollback_bulk_edit` - Rollback edits
- `validate_bulk_edit` - Validate edits
- `estimate_bulk_impact` - Estimate impact
- `get_bulk_file_statistics` - Get statistics
- `get_available_rollbacks` - List rollbacks
- `execute_bulk_operation` - Execute bulk op

#### 6. Stream Processing (10 tools)
- `stream_process_file` - Process file as stream
- `cancel_stream_operation` - Cancel stream op
- `resume_stream_operation` - Resume stream op
- `list_stream_operations` - List operations
- `cleanup_stream_operations` - Cleanup ops
- `get_stream_progress` - Get progress
- `get_available_processors` - List processors
- `estimate_stream_processing` - Estimate processing
- `process_stream` - Process stream
- `monitor_stream` - Monitor stream

#### 7. Architecture & Quality (15 tools)
- `validate_architecture` - Validate architecture
- `detect_layer_violations` - Detect violations
- `get_architecture_report` - Get report
- `define_custom_architecture` - Define architecture
- `generate_architecture_diagram` - Generate diagram
- `get_architecture_recommendations` - Get recommendations
- `get_predefined_architectures` - List architectures
- `detect_god_classes` - Detect god classes
- `detect_god_methods` - Detect god methods
- `detect_duplicates` - Detect duplicates
- `detect_breaking_changes` - Detect breaking changes
- `compare_code_blocks` - Compare code
- `get_duplicate_hotspots` - Get duplication hotspots
- `get_complexity_metrics` - Get complexity metrics
- `check_type_compliance` - Check type compliance

#### 8. Roslyn Analyzers (6 tools)
- `load_roslyn_analyzers` - Load analyzers
- `list_roslyn_analyzers` - List analyzers
- `run_roslyn_analyzers` - Run analyzers
- `apply_roslyn_fixes` - Apply fixes
- `configure_analyzer` - Configure analyzer
- `get_analyzer_config` - Get config
- `reset_analyzer_config` - Reset config

#### 9. Code Quality (3 tools)
- `code_quality_analyze` - Run analysis
- `code_quality_fix` - Apply fixes
- `code_quality_profiles` - Get profiles

#### 10. Configuration & Workflow (7 tools)
- `get_workflows` - Get GitHub workflows
- `parse_workflow` - Parse workflow file
- `validate_workflow_consistency` - Validate workflow
- `get_config_schema` - Get config schema
- `merge_configs` - Merge configurations
- `parse_project` - Parse project file
- `trace_feature` - Trace feature implementation

#### 11. Migration Tools (6 tools)
- `analyze_migrations` - Analyze migrations
- `validate_migrations` - Validate migrations
- `get_migration_history` - Get history
- `get_migration_dependencies` - Get dependencies
- `generate_migration_report` - Generate report
- `detect_breaking_changes` - Detect breaking changes

#### 12. Report Generation (4 tools)
- `generate_analysis_report` - Analysis report
- `generate_optimization_plan` - Optimization plan
- `get_refactoring_suggestions` - Refactoring suggestions
- `get_optimization_recommendations` - Optimization recommendations

## Overlap Analysis Matrix

### Major Overlap Groups

#### Group 1: Find Operations (HIGH OVERLAP)
Current tools with overlapping functionality:
- `find_symbol`, `find_references`, `find_implementations`, `find_callers`, `find_call_chains`, `find_type_usages`, `find_unused_methods`, `find_recursive_calls`

**Overlap**: All search for code elements with different scopes
**Redundancy Level**: 70%

#### Group 2: Analysis Operations (EXTREME OVERLAP)
Current tools with overlapping functionality:
- 17+ `analyze_*` tools with significant overlap
- Many do similar AST analysis with different focus

**Overlap**: Similar analysis engines, different output filters
**Redundancy Level**: 80%

#### Group 3: Bulk Operations (MODERATE OVERLAP)
- Multiple bulk tools that could share infrastructure
- Separate preview, execute, rollback could be modes

**Overlap**: Same bulk processing engine
**Redundancy Level**: 50%

#### Group 4: Code Quality Tools (COMPLETE OVERLAP)
- `code_quality_analyze` vs individual analyzers
- `detect_*` tools vs quality analysis

**Overlap**: Same underlying analysis
**Redundancy Level**: 90%

#### Group 5: File Operations (POTENTIAL OVERLAP)
- `file_edit` vs `add_class_property`/`add_class_method`
- Could unify under semantic edit API

**Overlap**: Different abstraction levels of same operation
**Redundancy Level**: 40%

## Consolidation Proposals

### Proposal A: Unified Search API (Reduce 15 → 3 tools)

**Before**: 15 find/search tools
**After**: 3 tools

```yaml
# Tool 1: Universal Find
find:
  target: symbol|reference|caller|implementation|usage|duplicate
  name: "string"
  options:
    include_indirect: bool
    max_depth: int
    scope: file|project|namespace

# Tool 2: Analyze Dependencies
analyze_dependencies:
  type: circular|recursive|unused|call_chain
  target: "method/type/namespace"

# Tool 3: Search Text (for non-semantic search)
search_text:
  pattern: "string"
  regex: bool
```

**Savings**: 12 tools eliminated

### Proposal B: Unified Analysis API (Reduce 30 → 5 tools)

**Before**: 30+ analyze_* tools
**After**: 5 tools

```yaml
# Tool 1: Code Analysis
analyze:
  type: quality|complexity|duplication|dependencies|architecture
  target: file|symbol|project
  profile: default|strict|performance

# Tool 2: Architecture Validation
validate_architecture:
  rules: [...] # Combines all architecture tools

# Tool 3: Migration Analysis
analyze_migrations:
  check: all|breaking|dependencies|history

# Tool 4: Impact Analysis
analyze_impact:
  change: "path/symbol"
  scope: direct|transitive|full

# Tool 5: Generate Report
generate_report:
  type: analysis|optimization|refactoring|architecture
  format: json|markdown|html
```

**Savings**: 25 tools eliminated

### Proposal C: Unified Edit API (Reduce 10 → 3 tools)

**Before**: 10 editing tools
**After**: 3 tools

```yaml
# Tool 1: Semantic Edit
edit:
  type: file|class|method|property
  operation: add|modify|delete|refactor
  target: "path/symbol"
  changes: [...]

# Tool 2: Bulk Operations
bulk_operation:
  type: replace|refactor|transform
  preview: bool
  rollback_id: "string" (optional)

# Tool 3: Stream Process (for large files)
stream_process:
  operation: transform|analyze|optimize
  options: {...}
```

**Savings**: 7 tools eliminated

### Proposal D: Auto-Loading Strategy

**Current Pain Points**:
1. Must call `load_roslyn_analyzers` before analysis
2. Must call `project_open` before any operation
3. Must configure analyzers separately

**Proposed Auto-Loading**:
```yaml
# Automatic behaviors:
1. Auto-open project from MCP working directory
2. Lazy-load Roslyn on first analysis request
3. Auto-discover analyzers from NuGet cache
4. Smart defaults for all operations

# Configuration (optional):
mcp_config:
  auto_load:
    project: true  # Auto-open on startup
    analyzers: true  # Auto-load on first use
    workspace: lazy  # Load Roslyn on demand
  defaults:
    analysis_profile: "recommended"
    bulk_preview: true
    stream_chunk_size: 1000
```

**Benefits**:
- Zero configuration for common cases
- Faster perceived startup
- Progressive enhancement
- Power users can still customize

### Proposal E: Consolidated Tool Registry

**Final Consolidated Tool Set (Target: 50-60 tools)**

#### Core Tools (10) - Keep Separate
1. `project` - Project operations (open/info combined)
2. `file` - File operations (list/read/write/edit)
3. `find` - Universal search
4. `analyze` - Universal analysis
5. `edit` - Semantic editing
6. `bulk` - Bulk operations
7. `stream` - Stream processing
8. `validate` - Validation operations
9. `report` - Report generation
10. `config` - Configuration management

#### Specialized Tools (40) - Domain-Specific
- Architecture validation (5)
- Migration analysis (5)
- Code quality (5)
- Roslyn integration (5)
- Workflow/Config analysis (5)
- Advanced refactoring (5)
- Performance optimization (5)
- Debugging/Tracing (5)

Total: ~50 tools (58% reduction)

## Implementation Plan

### Phase 1: Create Unified APIs (Week 1-2)
1. Implement unified `find` tool with mode parameter
2. Implement unified `analyze` tool with type parameter
3. Add backward compatibility aliases

### Phase 2: Auto-Loading (Week 3)
1. Implement project auto-detection
2. Add lazy Roslyn loading
3. Create configuration system
4. Add analyzer auto-discovery

### Phase 3: Consolidate Bulk/Stream (Week 4)
1. Merge bulk operations into single tool
2. Unify stream processing
3. Add operation pipelines

### Phase 4: Migration & Documentation (Week 5)
1. Create migration guide
2. Add deprecation warnings
3. Update all documentation
4. Create tool discovery helper

## Migration Strategy

### Backward Compatibility
```csharp
// Old tools become aliases
"find_references" → redirect to "find" with target="reference"
"analyze_impact" → redirect to "analyze" with type="impact"

// Deprecation warnings
if (IsDeprecatedTool(toolName)) {
    logger.LogWarning($"Tool '{toolName}' is deprecated. Use '{newTool}' instead.");
    // Still execute for compatibility
}
```

### Progressive Migration
1. **v1.0**: Add new unified tools, keep old ones
2. **v1.1**: Mark old tools as deprecated
3. **v1.2**: Hide deprecated tools from listing
4. **v2.0**: Remove deprecated tools

## User Experience Improvements

### Before (Complex)
```json
// Must remember 119 tools and their exact names
{"tool": "load_roslyn_analyzers"}
{"tool": "project_open", "path": "/src"}
{"tool": "find_callers", "method": "Process"}
{"tool": "analyze_call_patterns", "method": "Process"}
{"tool": "detect_circular_dependencies"}
{"tool": "generate_analysis_report"}
```

### After (Simple)
```json
// Auto-loads, smart defaults, unified APIs
{"tool": "find", "target": "caller", "name": "Process"}
{"tool": "analyze", "type": "patterns", "target": "Process"}
{"tool": "report", "type": "analysis"}
```

### Tool Discovery Helper
```json
// New discovery tool
{"tool": "help", "query": "find methods"}
// Returns: "Use 'find' tool with target='symbol' and kind='method'"

{"tool": "suggest", "goal": "refactor large class"}
// Returns suggested tool sequence with parameters
```

## Success Metrics

### Quantitative Goals
- **Tool Count**: 119 → 50-60 (50% reduction)
- **Common Operations**: 6 tools → 2 tools (66% reduction)
- **Setup Steps**: 3-4 → 0 (100% automation)
- **API Surface**: 119 endpoints → 50 endpoints

### Qualitative Goals
- **Discoverability**: Can guess tool names
- **Consistency**: Similar operations use similar APIs
- **Power**: No functionality loss
- **Performance**: Faster through lazy loading
- **Learning Curve**: 80% of users need only 10 tools

## Recommendations

### Immediate Actions
1. **Stop adding new specialized tools** - Use parameters instead
2. **Start implementing unified APIs** - Begin with find/analyze
3. **Add auto-loading** - Quick win for UX
4. **Document patterns** - Help users understand consolidation

### Long-term Strategy
1. **Version 1.5**: Consolidated APIs with compatibility
2. **Version 2.0**: Clean removal of deprecated tools
3. **Version 3.0**: Pipeline/workflow support
4. **Continuous**: Monitor usage patterns, adjust

## Conclusion

MCPsharp's 119 tools can be effectively consolidated to 50-60 tools through:
1. **Unified APIs** with mode/type parameters
2. **Auto-loading** for zero-configuration startup
3. **Smart defaults** for common operations
4. **Progressive migration** maintaining compatibility

This 50% reduction in tool count will significantly improve usability while maintaining all current functionality and power-user capabilities.