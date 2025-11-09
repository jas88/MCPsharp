# MCPsharp: Comprehensive Search & Edit Capabilities Inventory

## Executive Summary

MCPsharp provides a **mature but incomplete** search and editing infrastructure. It has **strong semantic/AST-based search** via Roslyn, **comprehensive bulk editing**, and **advanced code analysis**, but lacks **text-based search capabilities** (regex/grep-like full-text search). The architecture is well-organized with 60+ MCP tools exposed across multiple service layers.

---

## 1. EXISTING SEARCH CAPABILITIES

### 1.1 Semantic/Symbol-Based Search (Strong)

#### Tools Available:
- **find_symbol** - Locate symbols (classes, methods, properties) by name with optional kind filtering
- **find_references** - Find all references to a symbol (both by name and by location)
- **find_implementations** - Find all implementations of interfaces/abstract classes
- **find_callers** - Reverse search: who calls a specific method (supports direct/indirect)
- **find_call_chains** - Bidirectional call chain analysis (forward/backward)
- **find_type_usages** - Find all usages of a specific type
- **find_unused_methods** - Identify methods with no callers (dead code detection)
- **find_recursive_calls** - Find recursion patterns in methods
- **find_circular_dependencies** - Detect circular dependency patterns

#### Services Behind These Tools:
- **SymbolQueryService** - Symbol lookup by name with kind filtering
- **ReferenceFinderService** - Reference analysis using Roslyn SymbolFinder
- **AdvancedReferenceFinderService** - Facade for coordinating multiple search services
- **CallerAnalysisService** - Backward search (find who calls X)
- **CallChainService** - Call graph and chain analysis (10-depth default, configurable)
- **TypeUsageService** - Type reference tracking across codebase

#### Capabilities:
- Location-based lookup (file path + line/column)
- Name-based lookup
- Kind filtering (class, method, property, interface, etc.)
- Distinction between direct and indirect references
- Confidence levels for call detection (AST-based, invocation-based, etc.)
- Handles source generators transparently

#### Limitations:
- **No text/regex search** - Cannot search for arbitrary text patterns
- **No grep-like functionality** - Cannot search file contents for strings
- **Semantic only** - Requires compilation; won't find partial matches

---

### 1.2 Type & Inheritance Analysis (Strong)

#### Tools Available:
- **analyze_inheritance** - Map inheritance hierarchies (parent classes, interfaces)
- **analyze_type_dependencies** - Find what a type depends on
- **get_class_structure** - Complete class member introspection
- **get_symbol_info** - Detailed symbol information at location

#### Services:
- **ClassStructureService** - Get class members (properties, methods, nested types)

#### Capabilities:
- Full type hierarchy mapping
- Interface implementation tracking
- Dependency graph for types
- Member introspection (visibility, modifiers, etc.)

---

### 1.3 Call Graph & Dependency Analysis (Strong)

#### Tools Available:
- **analyze_call_graph** - Build call graphs for types/namespaces
- **analyze_call_patterns** - Analyze how methods are called
- **find_call_chains** - Trace method call sequences
- **find_circular_dependencies** - Detect circular reference issues

#### Services:
- **CallChainService** - Call chain exploration (configurable max depth: default 10-20)

#### Capabilities:
- Bidirectional analysis (what calls X, what X calls)
- Confidence scoring for detected calls
- Call type classification (direct, invocation, delegate, etc.)
- Shortest path finding between methods
- Reachability analysis

---

### 1.4 Cross-File Impact & Feature Tracing (Moderate)

#### Tools Available:
- **analyze_impact** - Predict what changes when code changes (C#, config, workflows, docs)
- **trace_feature** - Find all code implementing a feature (multi-file)

#### Services:
- **ImpactAnalyzerService** - Combines C#, config, workflow, and documentation impacts
- **FeatureTracerService** - Maps features to implementation files

#### Capabilities:
- Multi-language impact analysis
- Workflow/config impact detection
- Feature component mapping (controller, service, repository, models, migrations, tests)
- Documentation correlation

#### Limitations:
- Pattern-based rather than AST-based for non-C# files
- Requires heuristic matching for feature identification

---

## 2. EXISTING EDIT CAPABILITIES

### 2.1 Text-Based Editing (Basic)

#### Tools:
- **file_edit** - Apply text edits (replace, insert, delete operations)
- **file_write** - Write entire file content
- **file_read** - Read file contents

#### Implementation:
- Simple text-based operations
- No semantic awareness (text line/position based)
- Support for batch operations via arrays

#### Limitations:
- Brittle for large files (line/column changes invalidate later edits)
- No AST awareness - can produce invalid code
- Manual offset calculation required

---

### 2.2 Semantic/AST-Aware Editing (Strong)

#### Tools:
- **add_class_property** - Add properties to classes with proper syntax
- **add_class_method** - Add methods with parameter and body support
- **file_edit** - (When used via SemanticEditService)

#### Services:
- **SemanticEditService** - AST-aware edits using Roslyn syntax rewriting
  - Finds insertion points (after properties, before methods)
  - Generates proper syntax nodes
  - Maintains code style consistency
  - Validates class existence and location

#### Capabilities:
- Generate properties with getters/setters
- Generate method stubs with parameters
- Proper indentation and formatting
- Respects accessibility modifiers (public, private, protected, internal)

#### Limitations:
- Limited to adding properties/methods
- Cannot refactor existing code
- Cannot restructure large blocks

---

### 2.3 Bulk Editing Across Files (Strong)

#### Tools:
- **bulk_replace** - Regex replace across multiple files in parallel
- **bulk_transform** - Transform files using processors
- **multi_file_edit** - Edit multiple files with different patterns
- **conditional_edit** - Apply edits conditionally based on file content
- **batch_refactor** - Structured refactoring across files

#### Advanced Bulk Tools:
- **preview_bulk_changes** - Preview changes before applying
- **validate_bulk_edit** - Validate bulk edits
- **rollback_bulk_edit** - Undo bulk operations
- **get_available_rollbacks** - List undoable operations
- **estimate_bulk_impact** - Predict bulk edit consequences
- **get_bulk_file_statistics** - Pre-edit analysis

#### Services:
- **BulkEditService** - Parallel processing with:
  - Regex pattern support (with validation)
  - Backup creation (automatic rollback points)
  - Preview mode (dry-run)
  - File exclusion patterns
  - Parallel execution (CPU-count configurable)
  - Progress tracking
  - Error handling and reporting

#### Capabilities:
- Regex replacement patterns
- Exclusion lists
- Parallel processing
- Atomic operations with rollback
- Backup management
- Pre-execution validation
- Statistics collection

#### Implementation Details:
- Uses `System.Text.RegularExpressions.Regex` with validation
- Concurrent processing with semaphore limiting
- Temporary backup storage with MD5 checksums
- Session-based rollback tracking

---

## 3. SPECIALIZED SEARCH CAPABILITIES

### 3.1 Code Quality & Static Analysis

#### Tools:
- **code_quality_analyze** - Scan for code quality issues
- **code_quality_fix** - Apply automated fixes
- **code_quality_profiles** - Manage analysis profiles
- **run_roslyn_analyzers** - Execute Roslyn diagnostics
- **load_roslyn_analyzers** - Load custom analyzers
- **list_roslyn_analyzers** - List available analyzers

#### Services:
- **RoslynAnalyzerService** - Roslyn-based diagnostic collection
- **AnalyzerRegistry** - Manage built-in and custom analyzers
- **BuiltInCodeFixRegistry** - Automated fixes for:
  - Async/await patterns
  - Exception logging
  - Unused code
  - Static method recommendations

#### Capabilities:
- Roslyn diagnostic detection
- Configurable analyzer profiles
- Code fix automation
- Analysis caching
- Report generation

---

### 3.2 Duplicate Code Detection

#### Tools:
- **detect_duplicates** - Find duplicate code blocks
- **find_exact_duplicates** - Identical code sequences
- **find_near_duplicates** - Similar code patterns
- **analyze_duplication_metrics** - Quantify duplication
- **get_duplicate_hotspots** - Find heavily duplicated areas
- **compare_code_blocks** - Compare two code segments
- **get_refactoring_suggestions** - Suggest extract-method opportunities
- **validate_refactoring** - Verify refactoring safety

#### Services:
- **DuplicateCodeDetectorService** - Syntax-based duplicate detection

---

### 3.3 Architecture & Complexity Analysis

#### Tools:
- **validate_architecture** - Check against defined layers
- **detect_layer_violations** - Find architecture rule breaks
- **get_architecture_report** - Generate architecture summary
- **analyze_circular_dependencies** - Detect circular references
- **get_architecture_recommendations** - Suggest improvements
- **check_type_compliance** - Validate type placement
- **get_predefined_architectures** - List template architectures
- **define_custom_architecture** - Define custom layer rules

#### Services:
- **ArchitectureValidatorService** - Layer definition and validation

#### Capabilities:
- Predefined architectures (Clean, Onion, Hexagonal, etc.)
- Custom layer definitions
- Violation reporting
- Diagram generation (planned)

---

### 3.4 Large File & Complexity Analysis

#### Tools (Phase 3 - Placeholders):
- **analyze_large_files** - Identify large files
- **optimize_large_class** - Suggest class refactoring
- **optimize_large_method** - Suggest method splitting
- **detect_god_classes** - Find overly large classes
- **detect_god_methods** - Find overly large methods
- **analyze_code_smells** - General smell detection
- **get_complexity_metrics** - Cyclomatic complexity, etc.
- **generate_optimization_plan** - Refactoring roadmap

#### Status:
- Currently placeholders (returns coming-soon message)
- Infrastructure in place

---

### 3.5 Configuration & Workflow Analysis

#### Tools:
- **get_workflows** - List GitHub Actions workflows
- **parse_workflow** - Parse workflow YAML structure
- **validate_workflow_consistency** - Check workflow-code alignment
- **get_config_schema** - Introspect config file structure
- **merge_configs** - Combine configuration files

#### Services:
- **ConfigAnalyzerService** - JSON/YAML config parsing
- **WorkflowAnalyzerService** - GitHub Actions YAML parsing

#### Capabilities:
- YAML/JSON schema extraction
- Workflow definition parsing
- Config consistency checking
- Multi-file config merging

---

## 4. STREAMING & LARGE-FILE PROCESSING

#### Tools:
- **stream_process_file** - Process files using streaming processors
- **get_stream_progress** - Monitor streaming operations
- **cancel_stream_operation** - Stop in-progress streams
- **resume_stream_operation** - Resume paused streams
- **list_stream_operations** - List active streams
- **cleanup_stream_operations** - Clean up completed streams
- **get_available_processors** - List available stream processors
- **estimate_stream_processing** - Estimate stream operation cost

#### Services:
- **StreamingFileProcessor** - Process large files incrementally
- **StreamOperationManager** - Manage operation lifecycle

#### Capabilities:
- Streaming for large file processing
- Progress tracking
- Operation cancellation and resumption
- Memory-efficient processing

---

## 5. CONSOLIDATED/UNIFIED SERVICES

#### High-Level Tools:
- **analyze_symbol** - Unified symbol analysis
- **analyze_type** - Unified type analysis
- **analyze_file** - Unified file analysis
- **analyze_project** - Unified project analysis
- **analyze_architecture** - Unified architecture analysis
- **analyze_dependencies** - Unified dependency analysis
- **analyze_quality** - Unified quality analysis

#### Services:
- **UnifiedAnalysisService** - Facade coordinating multiple analyzers
- **UniversalFileOperations** - File ops abstraction
- **BulkOperationsHub** - Coordinate bulk operations
- **StreamProcessingController** - Manage streaming

---

## 6. CRITICAL GAPS & MISSING CAPABILITIES

### 6.1 Text-Based Search (HIGH PRIORITY)

**Missing:**
- No full-text search / grep-like functionality
- No regex literal search in file contents
- No case-sensitive/insensitive text search
- No multi-file text pattern matching

**Impact:**
- Cannot search for arbitrary strings across codebase
- Must use semantic search (limited to valid C# symbols)
- Cannot find string literals, comments, documentation text

**Use Cases Blocked:**
- "Find all TODO comments" → Not possible
- "Find string 'connection_string' in configs" → Not possible
- "Search for deprecated method name in comments" → Not possible
- "Find all references to magic number 42" → Not possible

**Implementation Approach:**
Would need:
1. RipGrep / UGrep integration OR built-in regex file scanner
2. Pattern: `search_text(pattern, fileGlob, caseSensitive, regex)`
3. Limited results (pagination) due to response size
4. Performance optimization (streaming results)

---

### 6.2 Refactoring Operations (MEDIUM PRIORITY)

**Missing:**
- No rename symbol (requires cross-file updates)
- No extract method
- No extract class
- No move type to different file
- No split class/module
- No inline variable/method
- No convert to lambda

**Impact:**
- Large refactorings must be done manually
- Cannot safely rename across project

**What Exists:**
- Add property/method (forward engineering only)
- Bulk replace (text-based, fragile)

---

### 6.3 Code Navigation Gaps (MEDIUM PRIORITY)

**Missing:**
- No "go to definition" (only "find references")
- No "go to implementation" for interfaces
- No "find all overrides" of a virtual method
- No "find all overloads" of a method
- No "show base class" navigation

**What Exists:**
- find_references (find usages)
- find_implementations (find implementing classes)
- analyze_inheritance (get type hierarchy)

---

### 6.4 Advanced Refactoring Tools (MEDIUM PRIORITY)

**Missing:**
- No automated code fix application (beyond bulk_replace)
- No safe method extraction
- No safe variable extraction
- No struct-to-record conversion
- No null-safety refactoring

**What Exists:**
- RoslynAnalyzerService detects issues
- Code fix framework in place
- Automated fixes for specific patterns (async/await, logging, unused code)

---

### 6.5 SQL Migration Analysis (LOWER PRIORITY)

**Status:** Phase 3 placeholder tools exist but not fully implemented
- analyze_migrations
- detect_breaking_changes  
- get_migration_history
- get_migration_dependencies
- generate_migration_report
- validate_migrations

---

### 6.6 Large File Optimization (LOWER PRIORITY)

**Status:** Phase 3 placeholder tools exist but not fully implemented
- analyze_large_files
- optimize_large_class
- optimize_large_method
- detect_god_classes
- detect_god_methods
- analyze_code_smells
- get_complexity_metrics

---

## 7. INVENTORY TABLE: Search Tools

| Tool Name | Category | Type | Status | Depends On |
|-----------|----------|------|--------|-----------|
| find_symbol | Symbol Search | Semantic | ✓ Complete | SymbolQueryService |
| find_references | Symbol Search | Semantic | ✓ Complete | ReferenceFinderService |
| find_implementations | Symbol Search | Semantic | ✓ Complete | ReferenceFinderService |
| find_callers | Reverse Search | Semantic | ✓ Complete | CallerAnalysisService |
| find_call_chains | Reverse Search | Semantic | ✓ Complete | CallChainService |
| find_type_usages | Reverse Search | Semantic | ✓ Complete | TypeUsageService |
| analyze_call_patterns | Analysis | Semantic | ✓ Complete | CallChainService |
| analyze_inheritance | Analysis | Semantic | ✓ Complete | ClassStructureService |
| find_circular_dependencies | Analysis | Semantic | ✓ Complete | CallChainService |
| find_unused_methods | Code Quality | Semantic | ✓ Complete | ReferenceFinderService |
| analyze_call_graph | Analysis | Semantic | ✓ Complete | CallChainService |
| find_recursive_calls | Analysis | Semantic | ✓ Complete | CallChainService |
| analyze_type_dependencies | Analysis | Semantic | ✓ Complete | ClassStructureService |
| get_class_structure | Symbol Introspection | Semantic | ✓ Complete | ClassStructureService |
| get_symbol_info | Symbol Introspection | Semantic | ✓ Complete | ReferenceFinderService |
| analyze_impact | Impact Analysis | Multi-Language | ✓ Complete | ImpactAnalyzerService |
| trace_feature | Feature Navigation | Multi-Language | ✓ Complete | FeatureTracerService |
| run_roslyn_analyzers | Code Quality | Diagnostic | ✓ Complete | RoslynAnalyzerService |
| code_quality_analyze | Code Quality | Diagnostic | ✓ Complete | RoslynAnalyzerService |
| detect_duplicates | Code Quality | Syntactic | ✓ Complete | DuplicateCodeDetectorService |
| find_exact_duplicates | Code Quality | Syntactic | ✓ Complete | DuplicateCodeDetectorService |
| find_near_duplicates | Code Quality | Syntactic | ✓ Complete | DuplicateCodeDetectorService |
| get_config_schema | Config Analysis | Structural | ✓ Complete | ConfigAnalyzerService |
| parse_workflow | Workflow Analysis | Structural | ✓ Complete | WorkflowAnalyzerService |
| validate_architecture | Architecture | Structural | ✓ Complete | ArchitectureValidatorService |
| detect_layer_violations | Architecture | Structural | ✓ Complete | ArchitectureValidatorService |
| search_text | Text Search | **MISSING** | ❌ | N/A |

---

## 8. INVENTORY TABLE: Edit Tools

| Tool Name | Category | Capability | Status | Mechanism |
|-----------|----------|-----------|--------|-----------|
| file_edit | Basic Text Edit | Text operations | ✓ Complete | Line/column positioning |
| file_write | File Write | Full file replacement | ✓ Complete | Direct write |
| add_class_property | Code Generation | AST-aware | ✓ Complete | Roslyn syntax rewriting |
| add_class_method | Code Generation | AST-aware | ✓ Complete | Roslyn syntax rewriting |
| bulk_replace | Bulk Edit | Regex-based | ✓ Complete | Parallel regex replacement |
| bulk_transform | Bulk Edit | Streaming | ✓ Complete | Stream processors |
| multi_file_edit | Bulk Edit | Conditional | ✓ Complete | Pattern matching |
| conditional_edit | Bulk Edit | Conditional | ✓ Complete | Content-based conditions |
| batch_refactor | Bulk Edit | Structured | ✓ Complete | Multi-step transformations |
| preview_bulk_changes | Bulk Edit | Preview/Dry-run | ✓ Complete | Dry-run execution |
| rollback_bulk_edit | Bulk Edit | Undo | ✓ Complete | Session-based rollback |
| apply_roslyn_fixes | Code Generation | Fix application | ✓ Complete | Code fix framework |
| code_quality_fix | Code Generation | Fix application | ✓ Complete | Analyzer-based fixes |
| rename_symbol | Refactoring | **MISSING** | ❌ | N/A |
| extract_method | Refactoring | **MISSING** | ❌ | N/A |
| extract_class | Refactoring | **MISSING** | ❌ | N/A |
| move_type | Refactoring | **MISSING** | ❌ | N/A |

---

## 9. ARCHITECTURE OBSERVATIONS

### 9.1 Strengths
1. **Well-layered architecture**
   - Service layer (semantic operations)
   - MCP tool registry (marshaling)
   - Roslyn integration (AST foundation)

2. **Comprehensive Roslyn integration**
   - Direct Roslyn compilation access
   - Semantic model caching
   - Symbol finding infrastructure

3. **Scalability for bulk operations**
   - Parallel processing support
   - Streaming for large files
   - Memory-efficient operation handling

4. **Good error handling & safety**
   - Rollback support for bulk edits
   - Preview/dry-run mode
   - Backup creation before edits

5. **Cross-language awareness**
   - Understands C#, YAML, JSON, XML
   - Config and workflow analysis
   - Impact analysis across languages

### 9.2 Weaknesses
1. **Text search not implemented** - Major gap
2. **No refactoring operations** - Limited to add/bulk operations
3. **Phase 3 features incomplete** - SQL, large file optimization still placeholder
4. **Limited code generation** - Only properties/methods, no full classes
5. **Streaming processor architecture unclear** - Multiple services but unclear relationships

---

## 10. RECOMMENDATIONS FOR IMPROVEMENT

### Priority 1 (CRITICAL): Add Text Search
```csharp
// Proposed tool: search_text
"search_text" => await ExecuteSearchText(request.Arguments, ct)

// Parameters:
{
  "pattern": "string",           // Literal or regex pattern
  "fileGlob": "string",          // e.g., "**/*.cs", "**/*.json"
  "regex": bool,                 // If true, treat pattern as regex
  "caseSensitive": bool,         // Default: true
  "limit": int,                  // Max results (default 100)
  "contextLines": int            // Lines before/after match (default 2)
}

// Response: List of matches with file, line, context
```

**Implementation approach:**
1. Wrap System.Text.RegularExpressions or use built-in File I/O
2. Parallel file iteration with early termination
3. Pagination support for large result sets
4. Performance: 500-file project should respond in <2s

---

### Priority 2 (HIGH): Rename Symbol
```csharp
"rename_symbol" => await ExecuteRenameSymbol(request.Arguments, ct)

// Parameters:
{
  "symbolName": "string",        // Symbol to rename
  "newName": "string",           // New symbol name
  "containingType": "string?",   // Optional type filter
  "updateReferences": bool,      // Update all references (default: true)
  "preview": bool                // Dry-run mode
}
```

**Complexity:** High - needs Roslyn rewriter for all references

---

### Priority 3 (HIGH): Extract Method
```csharp
"extract_method" => await ExecuteExtractMethod(request.Arguments, ct)

// Parameters:
{
  "filePath": "string",
  "startLine": int,
  "endLine": int,
  "methodName": "string",
  "makePublic": bool,
  "preview": bool
}
```

**Complexity:** High - needs syntax rewriting and dependency analysis

---

### Priority 4 (MEDIUM): Enhance Code Generation
- Add class generation (not just property/method)
- Add interface generation
- Add record/struct generation
- Parameterized inheritance in generated code

---

### Priority 5 (MEDIUM): Complete Phase 3
- Implement SQL migration analysis
- Implement large file optimization
- Complete code smell detection

---

## 11. COMPARATIVE ANALYSIS

### vs. ReSharper/Visual Studio
- **Missing:** Rename refactoring, extract method, move type
- **Missing:** Code navigation (go-to-definition)
- **Have:** Better bulk operations, streaming support
- **Comparable:** Architecture validation, code quality analysis

### vs. OmniSharp LSP
- **Missing:** Text search (LSP `textDocument/references` for symbols exists)
- **Have:** Better bulk operations, cross-language analysis
- **Have:** Dedicated streaming/large-file support
- **Comparable:** Symbol search, code completion

### vs. ripgrep/ag (Text Search Tools)
- **Missing:** What we need! Pure text search not implemented
- **Have:** Semantic awareness (they don't)
- **Have:** AST-aware edits (they don't)

---

## 12. TOOL USAGE RECOMMENDATIONS

### For C# Code Analysis
- Use `find_symbol` + `find_references` for navigation
- Use `analyze_call_graph` for understanding method interactions
- Use `code_quality_analyze` for static analysis
- Use `detect_duplicates` for code quality metrics

### For Safe Editing
- Always use semantic tools (`add_class_property`, etc.) over `file_edit`
- Use `preview_bulk_changes` before `bulk_replace`
- Keep semantic edits atomic (one property/method at a time)

### For Large Refactorings
- Break into multiple `bulk_replace` operations with `preview_bulk_changes`
- Use `rollback_bulk_edit` if validation fails
- Consider `multi_file_edit` for conditional changes

### For Architecture Validation
- Start with `validate_architecture` to establish baseline
- Use `detect_layer_violations` for compliance checking
- Use `get_architecture_report` for documentation

---

## 13. CONCLUSION

**Current State:**
- 60+ MCP tools exposed
- Strong semantic/AST-based search and analysis
- Comprehensive bulk editing capabilities
- Well-architected service layer

**Critical Gaps:**
1. No text-based search (regex/grep)
2. No refactoring operations (rename, extract)
3. Phase 3 features incomplete

**Priority Order:**
1. Add text search capability (impacts many use cases)
2. Implement rename symbol (most common refactoring)
3. Implement extract method (code organization)
4. Complete Phase 3 features
5. Add code generation for classes/interfaces

**Overall Assessment:** **7.5/10**
- Excellent foundation for AST-based work
- Bulk editing is mature
- Text search is critical missing piece
- Refactoring tools would complete the picture

