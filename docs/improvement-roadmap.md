# MCPsharp Improvement Roadmap

**Date**: 2025-01-06
**Status**: Draft - Comprehensive Analysis Complete
**Scope**: Efficiency, Completeness, Robustness + AST Editing Implementation

---

## Executive Summary

MCPsharp has achieved significant progress with 38 consolidated MCP tools and comprehensive C# semantic analysis through direct Roslyn integration. This roadmap addresses:

1. **Efficiency Issues** - Performance bottlenecks, caching gaps, redundant work
2. **Completeness Gaps** - Phase 3 features, missing implementations, test coverage
3. **Robustness Improvements** - Error handling, edge cases, developer experience
4. **AST Editing Implementation** - Semantic C#, XML, and YAML editing systems

**Key Findings:**

- ✅ Core architecture is solid (Roslyn direct integration, tool consolidation)
- ⚠️ No caching layer → repeated semantic queries are expensive
- ⚠️ No background analysis → blocking on first query
- ⚠️ Column indexing bugs impact edit reliability
- ⚠️ Phase 3 services incomplete (architecture validation, duplicate detection)
- ⚠️ Missing AST-based editing → current text edits are brittle

**Recommended Priority:**

1. **Critical Path** (Weeks 1-4): Caching + Column Fix + Core AST Editing
2. **High Value** (Weeks 5-8): Background Analysis + Complete Phase 3
3. **Enhancement** (Weeks 9-12): Advanced AST Editing + Testing + Polish

---

## Part 1: Efficiency Improvements

### 1.1 Semantic Model Caching ⚡ CRITICAL

**Problem**: Every semantic query rebuilds compilation and semantic models from scratch.

**Impact**:

- `find_references` on large project: 5-10s per query
- Repeated queries: 100% redundant work
- Poor developer experience

**Solution**: Three-tier caching strategy

```csharp
// src/Services/Caching/SemanticModelCache.cs
public sealed class SemanticModelCache
{
    private readonly ConcurrentDictionary<DocumentId, WeakReference<SemanticModel>> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<SemanticModel?> GetOrCreateAsync(
        Document document,
        CancellationToken ct)
    {
        if (_cache.TryGetValue(document.Id, out var weakRef) &&
            weakRef.TryGetTarget(out var cached))
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var model = await document.GetSemanticModelAsync(ct);
            if (model != null)
            {
                _cache[document.Id] = new WeakReference<SemanticModel>(model);
            }
            return model;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate(DocumentId docId) => _cache.TryRemove(docId, out _);
    public void InvalidateAll() => _cache.Clear();
}
```

**Cache Levels**:

1. **Compilation Cache** - Expensive, long-lived (workspace-level)
2. **SemanticModel Cache** - Per-document, invalidate on edit
3. **Symbol Resolution Cache** - Short-lived LRU for repeated symbol lookups

**Implementation**:

- Week 1: Implement SemanticModelCache
- Week 1: Integrate with RoslynWorkspace
- Week 2: Add invalidation hooks for document changes
- Week 2: Add compilation cache with weak references
- Week 3: Symbol resolution cache with LRU eviction
- Week 3: Performance benchmarks (expect 5-10x speedup for repeated queries)

**Effort**: 2-3 weeks
**Priority**: P0 (Critical)
**Expected Impact**: 80% reduction in repeated query time

---

### 1.2 Incremental Compilation Updates 🔄

**Problem**: Document edits invalidate entire compilation, forcing full rebuild.

**Current Behavior**:

```csharp
// User edits File.cs
await AddDocumentAsync(filePath); // Replaces document
_compilation = await project.GetCompilationAsync(); // FULL RECOMPILATION
```

**Solution**: Roslyn's incremental compilation support

```csharp
public async Task<bool> UpdateDocumentAsync(string filePath, string newContent, CancellationToken ct)
{
    var docId = _documentMap[filePath];
    var oldDocument = _workspace.CurrentSolution.GetDocument(docId);

    // Create new document with updated text
    var newDocument = oldDocument.WithText(SourceText.From(newContent));

    // Apply changes - Roslyn handles incremental compilation
    if (_workspace.TryApplyChanges(newDocument.Project.Solution))
    {
        // Only affected syntax trees are recompiled
        _compilation = await newDocument.Project.GetCompilationAsync(ct);

        // Invalidate cache only for this document
        _semanticModelCache.Invalidate(docId);

        return true;
    }

    return false;
}
```

**Benefits**:

- 90% faster for single-file edits (only recompile changed file + dependents)
- Preserve semantic model cache for unchanged files
- Better background analysis support

**Implementation**:

- Week 2: Add `UpdateDocumentAsync` to RoslynWorkspace
- Week 2: Track document version numbers for staleness detection
- Week 3: Integrate with file watcher (when implemented)
- Week 3: Add tests with large projects (>100 files)

**Effort**: 1.5 weeks
**Priority**: P0 (Critical for editor-like experience)
**Expected Impact**: 90% faster edit-analyze cycle

---

### 1.3 McpToolRegistry Refactoring 🏗️

**Problem**: 6,074-line switch statement is unmaintainable.

**Current State**:

```csharp
// src/Services/McpToolRegistry.cs (6074 lines!)
public async Task<ToolCallResult> ExecuteToolAsync(ToolCallRequest request, CancellationToken ct = default)
{
    switch (request.Name)
    {
        case "project_open": /* 100 lines */ break;
        case "file_read": /* 80 lines */ break;
        // ... 36 more cases
    }
}
```

**Solution**: Registry pattern with interface

```csharp
// src/Services/Tools/IToolHandler.cs
public interface IToolHandler
{
    string ToolName { get; }
    Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct);
}

// src/Services/Tools/FileReadToolHandler.cs
public sealed class FileReadToolHandler : IToolHandler
{
    private readonly FileOperationsService _fileOps;

    public string ToolName => "file_read";

    public async Task<object> ExecuteAsync(
        Dictionary<string, object> parameters,
        CancellationToken ct)
    {
        var path = parameters["path"].ToString();
        return await _fileOps.ReadFileAsync(path, ct);
    }
}

// src/Services/McpToolRegistry.cs (NEW - ~100 lines)
public sealed class McpToolRegistry
{
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;

    public McpToolRegistry(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.ToolName);
    }

    public async Task<ToolCallResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken ct)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            return ToolCallResult.Error($"Unknown tool: {toolName}");
        }

        try
        {
            var result = await handler.ExecuteAsync(parameters, ct);
            return ToolCallResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolCallResult.Error(ex.Message);
        }
    }
}
```

**Benefits**:

- Each tool in separate file (~100 lines each)
- Easy to add new tools (implement `IToolHandler`)
- Testable in isolation
- Clear dependency injection

**Implementation**:

- Week 4: Define `IToolHandler` interface
- Week 4-5: Extract 10 tools per day → 4 days for all 38
- Week 5: Update DI registration
- Week 5: Integration tests to ensure no regressions
- Week 6: Delete old McpToolRegistry.cs (6000 lines gone!)

**Effort**: 2-3 weeks
**Priority**: P1 (High - improves maintainability)
**Expected Impact**: 95% reduction in McpToolRegistry complexity

---

### 1.4 Response Token Limiting Optimization 📊

**Problem**: ResponseProcessor truncates after generation, wasting work.

**Current Approach**:

```csharp
// Generate full response (expensive)
var fullResult = await ExecuteToolAsync(...);

// Serialize to JSON
var json = JsonSerializer.Serialize(fullResult);

// THEN check size and truncate
if (EstimateTokens(json) > maxTokens)
{
    json = TruncateResponse(json, maxTokens);
}
```

**Solution**: Streaming serialization with budget tracking

```csharp
public sealed class BudgetedJsonWriter
{
    private readonly JsonWriter _writer;
    private readonly int _maxTokens;
    private int _currentTokens;

    public void WriteProperty(string name, object value)
    {
        var estimated = EstimateTokens(name, value);

        if (_currentTokens + estimated > _maxTokens)
        {
            WriteTokenLimitReached();
            return;
        }

        _writer.WritePropertyName(name);
        WriteValue(value);
        _currentTokens += estimated;
    }

    private void WriteTokenLimitReached()
    {
        _writer.WritePropertyName("__truncated__");
        _writer.WriteValue($"Response truncated at {_maxTokens} tokens");
    }
}
```

**Benefits**:

- Stop serialization when budget reached (don't waste CPU)
- Provide partial results faster
- More accurate truncation (per-property vs per-character)

**Implementation**:

- Week 3: Implement BudgetedJsonWriter
- Week 3: Integrate with tool handlers
- Week 3: Add tests with large responses
- Week 4: Benchmark (expect 50% faster for truncated responses)

**Effort**: 1 week
**Priority**: P2 (Medium - nice optimization)
**Expected Impact**: 50% faster for large responses that get truncated

---

### 1.5 Lazy Service Instantiation ⚙️

**Problem**: All services instantiated at startup, even if unused.

**Current**: Program.cs creates all services eagerly

```csharp
var workspace = new RoslynWorkspace(...);
var symbolQuery = new SymbolQueryService(workspace, ...);
var classStructure = new ClassStructureService(workspace, ...);
// ... 30+ more services (ALL created at startup)
```

**Solution**: Service factory pattern

```csharp
public sealed class ServiceFactory
{
    private readonly ConcurrentDictionary<Type, Lazy<object>> _services = new();

    public T GetService<T>() where T : class
    {
        var lazy = _services.GetOrAdd(typeof(T), _ => new Lazy<object>(CreateService<T>));
        return (T)lazy.Value;
    }

    private T CreateService<T>()
    {
        // Resolve dependencies and construct
        return Activator.CreateInstance<T>();
    }
}
```

**Benefits**:

- Faster startup (don't create unused services)
- Lower memory footprint
- Services created on-demand

**Implementation**:

- Week 4: Implement ServiceFactory
- Week 4: Update Program.cs to use factory
- Week 4: Benchmark startup time (expect 40% faster)

**Effort**: 3-4 days
**Priority**: P2 (Medium)
**Expected Impact**: 40% faster startup

---

## Part 2: Completeness Improvements

### 2.1 Fix Column Indexing Bug 🐛 CRITICAL

**Problem**: Edit operations fail with "Key not present" and "Index out of range" errors.

**Root Cause**: Inconsistent 0-based vs 1-based indexing in edit operations.

**Investigation Needed**:

1. Trace all column usage in edit paths
2. Determine if LSP spec requires 0-based (it does: lines and cols both 0-indexed)
3. Audit all conversion points

**Fix Locations**:

```csharp
// src/Services/FileOperationsService.cs
public FileEditResult EditFile(string path, List<TextEdit> edits)
{
    // AUDIT: Are these 0-based or 1-based?
    foreach (var edit in edits)
    {
        var line = edit.Line; // 0-based or 1-based?
        var column = edit.Column; // 0-based or 1-based?

        // Convert consistently
        var text = lines[line]; // 0-based array access
        var before = text.Substring(0, column); // Should be 0-based
        var after = text.Substring(column + edit.Length); // Should be 0-based
    }
}
```

**Solution Strategy**:

1. **Standardize on 0-based internally** (matches LSP, programming convention)
2. **Document at API boundaries** (XML doc comments state index base)
3. **Add validation** (reject column >= line.Length)
4. **Add conversion helpers** if external APIs use 1-based

**Implementation**:

- Week 1: Audit all edit code paths (FileOperationsService, SemanticEditService)
- Week 1: Add unit tests with explicit assertions about indexing
- Week 1: Fix all 1-based → 0-based conversions
- Week 1: Add IndexingMode enum if both needed
- Week 2: Integration tests with real edit scenarios
- Week 2: Document indexing convention in ARCHITECTURE.md

**Effort**: 1-2 weeks
**Priority**: P0 (Critical - blocks reliable editing)
**Expected Impact**: 100% of edit operations work reliably

---

### 2.2 Complete Phase 3: Architecture Validation 🏛️

**Status**: Code complete (~43KB), limited testing, not integrated

**Missing**:

- MCP tool registration in McpToolRegistry
- Comprehensive test suite
- Integration with consolidated analysis tools
- Documentation and examples

**Implementation**:

- Week 6: Add MCP tool handlers for architecture validation
- Week 6: Write test suite (predefined architectures, custom definitions)
- Week 7: Integration tests with real projects
- Week 7: Example workflows (detect layer violations, generate diagrams)
- Week 7: Document in user guide

**Tools to Expose**:

- `validate_architecture` - Check against predefined patterns
- `detect_layer_violations` - Find architectural violations
- `get_architecture_report` - Comprehensive report
- `analyze_circular_dependencies` - Find dependency cycles
- `generate_architecture_diagram` - Mermaid/PlantUML output

**Effort**: 2 weeks
**Priority**: P1 (High value for complex projects)

---

### 2.3 Complete Phase 3: Duplicate Code Detection 🔍

**Status**: Code complete (~98KB), limited testing, not integrated

**Missing**:

- Hash-based exact duplicate detection optimization
- Near-miss similarity tuning (configurable threshold)
- Refactoring suggestion quality improvements
- MCP tool integration

**Implementation**:

- Week 6: Optimize exact duplicate detection (hash-based)
- Week 6: Tune similarity algorithm (test with real codebases)
- Week 7: Improve refactoring suggestions (context-aware)
- Week 7: Add MCP tool handlers
- Week 7: Integration tests
- Week 8: Document usage patterns

**Tools to Expose**:

- `detect_duplicates` - Find all duplicates (exact + near-miss)
- `find_exact_duplicates` - Fast exact-only search
- `get_duplicate_hotspots` - Files with most duplication
- `get_refactoring_suggestions` - Actionable improvements

**Effort**: 2-3 weeks
**Priority**: P1 (High value for code quality)

---

### 2.4 Complete Consolidated Tool Integration ✅

**Status**: Services implemented, not all wired to McpToolRegistry

**Missing Tools** (need MCP exposure):

- `UniversalFileOperations`: 4 tools partially wired
- `UnifiedAnalysisService`: 15 tools need full integration
- `BulkOperationsHub`: 8 tools need testing
- `StreamProcessingController`: 6 tools need testing

**Implementation**:

- Week 8: Extract all consolidated tools to IToolHandler implementations
- Week 8: Add integration tests for each
- Week 9: Performance benchmarks vs old tools
- Week 9: Update documentation with new unified APIs

**Effort**: 2 weeks
**Priority**: P1 (Complete tool consolidation effort)

---

### 2.5 Background Analysis State Machine 🔄

**Status**: Not implemented (planned in original design)

**Design** (from CLAUDE.md):

```text
Idle → Initializing → Loaded → FullyAnalyzed
```

**Benefits**:

- Return quick results immediately (project structure, file list)
- Fill in semantic details in background
- Better UX for large projects

**Implementation**:

```csharp
// src/Services/BackgroundAnalysis/AnalysisStateMachine.cs
public sealed class BackgroundAnalysisManager
{
    private AnalysisState _state = AnalysisState.Idle;
    private readonly RoslynWorkspace _workspace;

    public async Task InitializeAsync(string projectPath, CancellationToken ct)
    {
        _state = AnalysisState.Initializing;

        // Phase 1: Quick load (file list, project structure)
        await LoadProjectStructureAsync(projectPath, ct);
        _state = AnalysisState.Loaded;

        // Phase 2: Background compilation
        _ = Task.Run(async () =>
        {
            await _workspace.InitializeAsync(projectPath, ct);
            _state = AnalysisState.FullyAnalyzed;
        }, ct);
    }

    public bool IsFullyAnalyzed => _state == AnalysisState.FullyAnalyzed;
}
```

**Implementation**:

- Week 9: Implement AnalysisStateMachine
- Week 9: Add file watcher integration
- Week 10: Incremental re-analysis on file changes
- Week 10: Progress reporting for long operations

**Effort**: 2 weeks
**Priority**: P2 (Medium - UX improvement)

---

### 2.6 Testing Improvements 🧪

**Current Coverage**: ~84% (117/139 tests passing)

**Gaps**:

- MCP protocol integration (JSON-RPC over stdio)
- Consolidated tools (UniversalFileOperations, UnifiedAnalysisService)
- Phase 3 services (architecture, duplicate detection)
- Edit operations (column indexing tests!)
- Large-scale stress tests (1000+ file projects)

**Test Plan**:

#### Week 10-11: Critical Path Tests

- Edit operation tests (especially column indexing)
- MCP protocol end-to-end tests
- Consolidated tool integration tests

#### Week 11-12: Coverage Expansion

- Phase 3 service tests
- Large project stress tests (RDMP, RoslynAnalyzers)
- Performance regression tests

**Target**: 95%+ code coverage, 100% critical path coverage

**Effort**: 2-3 weeks
**Priority**: P1 (Essential for reliability)

---

## Part 3: AST-Based Editing Implementation

### 3.1 C# Semantic Editing 🎯 HIGH PRIORITY

**Goal**: Replace brittle text edits with Roslyn AST manipulation

**Core Operations** (Priority Order):

**Phase 1: Member Operations** (Week 5-6)

- ✅ `AddMethodOperation` - Add method to class with proper indentation
- ✅ `AddPropertyOperation` - Add property with getter/setter
- ✅ `ModifyMethodOperation` - Change signature, body
- ✅ `RemoveMethodOperation` - Delete method safely

**Phase 2: Refactoring Operations** (Week 7-8)

- ✅ `RenameSymbolOperation` - Rename with cascading updates (Roslyn Renamer API)
- ✅ `ExtractMethodOperation` - Extract code into new method with data flow analysis
- ✅ `InlineMethodOperation` - Inline method calls
- ✅ `ExtractInterfaceOperation` - Generate interface from class

**Phase 3: Namespace/Using Operations** (Week 9)

- ✅ `ChangeNamespaceOperation` - Move type to new namespace
- ✅ `AddUsingOperation` - Smart using directive placement
- ✅ `OrganizeUsingsOperation` - Sort and remove unused

**Architecture** (from design doc):

```csharp
EditOrchestrator
  ├─ EditTransactionManager (backup/rollback)
  ├─ CompilationValidator (verify after edit)
  ├─ DependencyAnalyzer (impact analysis)
  └─ EditOperations (AddMethod, Rename, etc.)

Integration:
  RoslynWorkspace
    ├─ ApplyDocumentChangeAsync() - Apply edit
    ├─ WorkspaceChanged event - Invalidate caches
    └─ TryApplyChanges() - Roslyn's incremental compilation
```

**MCP Tools**:

- `edit_code_semantic` - Apply single operation
- `preview_edit` - Dry-run with diff
- `batch_edit` - Multiple operations in transaction

**Implementation Timeline**:

- Week 5-6: Foundation + Member Operations (4 operations)
- Week 7-8: Refactoring Operations (4 operations)
- Week 9: Namespace Operations (3 operations)
- Week 10: MCP integration + testing
- Week 11: Documentation + examples

**Effort**: 7 weeks
**Priority**: P0 (Critical for reliable editing)
**Expected Impact**: 95%+ edit success rate (vs ~70% with text edits)

---

### 3.2 XML Project File Editing 📦

**Goal**: Semantic .csproj editing with MSBuild awareness

**Core Operations**:

**Phase 1: Basic Project Edits** (Week 8-9)

- ✅ `AddPackageReferenceOperation` - Add NuGet package
- ✅ `UpdateTargetFrameworkOperation` - Change target framework
- ✅ `ModifyPropertyOperation` - Set MSBuild property
- ✅ `AddProjectReferenceOperation` - Add project reference

**Phase 2: Advanced Features** (Week 10-11)

- ✅ Central Package Management support
- ✅ Directory.Build.props editing
- ✅ Conditional ItemGroup handling
- ✅ Multi-targeting support

**Phase 3: Integration** (Week 11-12)

- ✅ Roslyn workspace reload after edit
- ✅ Package compatibility validation
- ✅ Breaking change detection
- ✅ Solution file editing (.sln)

**Architecture**:

```csharp
ProjectFileEditor
  ├─ XmlProjectDocument (XDocument wrapper)
  ├─ MSBuildPropertyEvaluator (condition evaluation)
  ├─ PackageResolver (NuGet API integration)
  └─ RoslynWorkspaceUpdater (trigger reload)
```

**MCP Tools**:

- `modify_project_file` - Generic edit operation
- `add_package_reference` - Add NuGet package
- `update_target_framework` - Change framework version
- `analyze_project_structure` - Project analysis

**Implementation Timeline**:

- Week 8-9: XML infrastructure + basic operations (4 operations)
- Week 10-11: Advanced features (CPM, Directory.Build.props)
- Week 11-12: Integration with Roslyn workspace
- Week 12: Testing + documentation

**Effort**: 5 weeks
**Priority**: P1 (High value for project management)

---

### 3.3 YAML Workflow Editing 🔧

**Goal**: Semantic GitHub Actions workflow editing

**Core Operations**:

**Phase 1: Job Operations** (Week 10-11)

- ✅ `AddJobEdit` - Add workflow job
- ✅ `RemoveJobEdit` - Remove job with dependency updates
- ✅ `ModifyJobEdit` - Change job properties

**Phase 2: Step Operations** (Week 11-12)

- ✅ `AddStepEdit` - Add step to job
- ✅ `ModifyStepEdit` - Change step properties
- ✅ `UpdateActionVersionEdit` - Update action versions

**Phase 3: Advanced Features** (Week 12)

- ✅ Matrix configuration editing
- ✅ Trigger management (on: push, etc.)
- ✅ Schema validation against GitHub Actions
- ✅ Action deprecation detection

**Architecture**:

```csharp
WorkflowEditorService
  ├─ YamlDocumentContext (YamlDotNet wrapper)
  │  ├─ YamlNavigator (XPath-like navigation)
  │  ├─ CommentStore (preserve comments)
  │  └─ FormattingOptions (preserve style)
  ├─ WorkflowSchemaValidator (GitHub Actions schema)
  └─ ActionMetadataResolver (version checking)
```

**MCP Tools**:

- `edit_workflow` - Apply edits to workflow
- `add_workflow_job` - Add job with steps
- `update_action_versions` - Bulk version updates
- `modify_workflow_matrix` - Matrix configuration
- `validate_workflow` - Schema validation

**Implementation Timeline**:

- Week 10-11: YAML infrastructure + job operations (3 operations)
- Week 11-12: Step operations + action versioning (3 operations)
- Week 12: Advanced features (matrix, validation)
- Week 13: Testing + documentation

**Effort**: 3-4 weeks
**Priority**: P2 (Medium - nice for CI/CD management)

---

## Part 4: Robustness Improvements

### 4.1 Error Messages & Developer Experience 💬

**Current Issues**:

- Generic error messages ("Edit failed")
- No actionable guidance
- Stack traces exposed to user

**Improvements**:

**Structured Error Responses**:

```csharp
public record DetailedError
{
    public string Code { get; init; } // E001, W002
    public string Message { get; init; } // User-friendly
    public string? Suggestion { get; init; } // How to fix
    public string? DocumentationUrl { get; init; }
    public Dictionary<string, object>? Context { get; init; }
}
```

**Example**:

```json
{
  "code": "E_COLUMN_OUT_OF_RANGE",
  "message": "Column index 45 exceeds line length 32",
  "suggestion": "Check that column indices are 0-based and within line bounds",
  "context": {
    "file": "/path/to/file.cs",
    "line": 10,
    "column": 45,
    "lineLength": 32
  }
}
```

**Implementation**:

- Week 3: Define error code taxonomy
- Week 3: Add suggestion database
- Week 4: Update all error paths
- Week 4: Add documentation links

**Effort**: 1 week
**Priority**: P1 (High - improves DX)

---

### 4.2 Timeout & Cancellation Support ⏱️

**Current**: Long operations can't be cancelled

**Solution**: Propagate CancellationToken everywhere

```csharp
// Add timeout wrapper
public async Task<T> WithTimeoutAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);
    try
    {
        return await operation(cts.Token);
    }
    catch (OperationCanceledException)
    {
        throw new TimeoutException($"Operation exceeded {timeout}");
    }
}
```

**Implementation**:

- Week 5: Audit all async operations for CancellationToken
- Week 5: Add timeout configuration
- Week 5: Add progress reporting for long operations

**Effort**: 1 week
**Priority**: P2 (Medium - UX improvement)

---

### 4.3 Edge Case Handling 🎲

**Identified Edge Cases**:

1. **Empty Files** - Handle gracefully
2. **Unicode/Encoding** - Respect file encoding
3. **Partial Classes** - Track all declarations
4. **Source Generators** - Don't edit generated files
5. **Large Files** - Stream processing for >10MB files
6. **Symbolic Links** - Follow or reject?

**Implementation**:

- Week 6: Add edge case test suite
- Week 6: Fix identified issues
- Week 7: Document known limitations

**Effort**: 1-2 weeks
**Priority**: P2 (Medium)

---

## Implementation Timeline

### Phased Rollout (12-Week Plan)

**Weeks 1-2: Critical Foundations** 🔴

- ✅ Semantic model caching
- ✅ Incremental compilation
- ✅ Fix column indexing bug
- ✅ Start C# AST editing foundation

**Weeks 3-4: Performance & Architecture** 🟡

- ✅ Response token optimization
- ✅ McpToolRegistry refactoring
- ✅ Lazy service instantiation
- ✅ Continue C# AST editing (member operations)

**Weeks 5-6: Core Editing + Phase 3** 🟢

- ✅ Complete C# AST editing (member + refactoring operations)
- ✅ Complete architecture validation
- ✅ Complete duplicate detection
- ✅ Start XML project editing

**Weeks 7-8: Advanced Editing** 🔵

- ✅ Complete C# AST editing (namespace operations)
- ✅ Complete XML project editing (basic operations)
- ✅ Consolidated tool integration
- ✅ Start YAML workflow editing

**Weeks 9-10: Polish & Integration** 🟣

- ✅ Background analysis state machine
- ✅ Complete XML project editing (advanced features)
- ✅ Complete YAML workflow editing (job operations)
- ✅ Testing improvements (Phase 1)

**Weeks 11-12: Testing & Documentation** ⚪

- ✅ Complete YAML workflow editing (advanced features)
- ✅ Testing improvements (Phase 2)
- ✅ Comprehensive documentation
- ✅ Performance benchmarks
- ✅ Example workflows

---

## Success Metrics

### Performance Targets

- ⚡ Repeated semantic queries: 80% faster (via caching)
- ⚡ Edit-analyze cycle: 90% faster (incremental compilation)
- ⚡ Startup time: 40% faster (lazy services)
- ⚡ Large response handling: 50% faster (streaming serialization)

### Reliability Targets

- 🎯 Edit success rate: 95%+ (AST-based editing)
- 🎯 Test pass rate: 100% (fix failing tests)
- 🎯 Code coverage: 95%+ (add missing tests)
- 🎯 Column indexing: 100% correct (fix bug)

### Completeness Targets

- ✅ Phase 3 services: 100% complete and tested
- ✅ AST editing: C# (11 operations), XML (8 operations), YAML (9 operations)
- ✅ Consolidated tools: 100% integrated
- ✅ Documentation: Complete API reference + examples

### Developer Experience Targets

- 💬 Error messages: 100% actionable with suggestions
- 💬 Response time: <1s for common queries
- 💬 Edit preview: Available for all operations
- 💬 Documentation: Examples for all MCP tools

---

## Risk Assessment

### High Risk Items

1. **C# AST Editing Complexity** - Roslyn API is large, edge cases numerous
   - *Mitigation*: Start with simple operations, extensive testing, iterate

2. **Column Indexing Fix** - May have cascading impacts
   - *Mitigation*: Comprehensive test suite, audit all edit paths

3. **Caching Invalidation** - Wrong cache invalidation → stale results
   - *Mitigation*: Conservative invalidation, workspace change events

### Medium Risk Items

1. **Background Analysis** - Complexity of state machine
   - *Mitigation*: Start simple (two states), add complexity iteratively

2. **XML MSBuild Evaluation** - Complex condition evaluation
   - *Mitigation*: Support common cases, document limitations

### Low Risk Items

1. **McpToolRegistry Refactoring** - Mechanical transformation
   - *Mitigation*: Thorough testing, gradual migration

2. **YAML Editing** - YamlDotNet is mature
   - *Mitigation*: Comment preservation may require workarounds

---

## Dependencies & Prerequisites

### External Dependencies

- **NuGet Packages** (already present):
  - Microsoft.CodeAnalysis.CSharp (Roslyn)
  - YamlDotNet
  - System.Xml.Linq

- **New Dependencies** (if needed):
  - Json.Schema.Net (for GitHub Actions schema validation)
  - DiffPlex (for edit previews)

### Infrastructure Requirements

- **GitHub API Access** (for action metadata) - Optional, cached
- **NuGet API Access** (for package validation) - Optional, cached
- **Local File System** - Required for all operations

### Team Prerequisites

- Roslyn API familiarity (C# AST editing)
- XDocument API familiarity (XML editing)
- YamlDotNet API familiarity (YAML editing)
- Testing infrastructure knowledge

---

## Appendix: Design Documents

Full architecture designs are available in:

1. **C# AST Editing**: Detailed in agent output (58KB design doc)
2. **XML Project Editing**: Detailed in agent output (45KB design doc)
3. **YAML Workflow Editing**: Detailed in agent output (52KB design doc)

These documents include:

- Complete class hierarchies
- Operation implementations
- MCP tool interfaces
- Testing strategies
- Edge case handling
- Integration approaches

---

## Next Steps

**Immediate Actions** (Week 1):

1. ✅ Review and approve this roadmap
2. ✅ Set up project tracking (GitHub issues/projects)
3. ✅ Begin Week 1 tasks:
   - Implement SemanticModelCache
   - Fix column indexing bug
   - Start C# AST editing foundation

**Communication**:

- Weekly progress updates
- Design review for major components
- Integration testing at phase boundaries

**Success Criteria**:

- All P0 items complete by Week 8
- All P1 items complete by Week 12
- Performance targets met
- Test coverage >95%

---

End of Roadmap
