# Implementation Plan: SQLite Cache & Enhanced MCP UX

## Overview

This plan implements persistent SQLite caching, MCP resources for project overview, multi-solution support, and enhanced UX guidance. Each subtask is designed to be independently implementable by a subagent.

## Design Decisions (from user requirements)

| Decision | Choice |
|----------|--------|
| Overview delivery | **MCP Resources** - `project://overview` resource |
| Database location | **User cache**: `~/.cache/mcpsharp/{project-hash}.db` |
| Database content | **Maximum** - symbols, cross-refs, hashes, analysis, structure |
| Multi-solution | **Load all** solutions into unified index |
| Subagent emphasis | **All of the above** - descriptions, prompts, resources |

---

## Phase 1: SQLite Database Foundation

### 1.1 Add SQLite NuGet Dependency
**File:** `src/MCPsharp/MCPsharp.csproj`
**Task:** Add `Microsoft.Data.Sqlite` package reference
**Acceptance:**
- Package added with appropriate version
- Project builds successfully

### 1.2 Create Database Schema Models
**File:** `src/MCPsharp/Models/Database/` (new directory)
**Files to create:**
- `DbProject.cs` - project metadata entity
- `DbFile.cs` - file tracking entity
- `DbSymbol.cs` - symbol definition entity
- `DbReference.cs` - cross-reference entity
- `DbAnalysisResult.cs` - cached analysis results

**Acceptance:**
- All entity classes with appropriate properties
- Nullable annotations correct
- No warnings

### 1.3 Create Database Schema & Migrations
**File:** `src/MCPsharp/Services/Database/ProjectDatabaseSchema.cs`
**Task:** Define SQL schema creation scripts
**Tables:**
```sql
projects (id, root_path_hash, root_path, name, opened_at, solution_count, project_count, file_count)
files (id, project_id, relative_path, content_hash, last_indexed, size_bytes, language)
symbols (id, file_id, name, kind, namespace, containing_type, line, column, end_line, end_column, accessibility, signature)
references (id, from_symbol_id, to_symbol_id, reference_kind, file_id, line, column)
analysis_results (id, file_id, diagnostic_id, severity, message, line, column, cached_at)
project_structure (id, project_id, key, value_json)
```
**Acceptance:**
- Schema SQL is valid
- Includes indexes on frequently queried columns
- Version tracking for migrations

### 1.4 Create ProjectDatabase Service - Core
**File:** `src/MCPsharp/Services/Database/ProjectDatabase.cs`
**Task:** Implement core database service
**Methods:**
- `OpenOrCreateAsync(projectRootPath)` - creates/opens DB in `~/.cache/mcpsharp/{hash}.db`
- `CloseAsync()` - closes connection gracefully
- `GetConnectionString(projectRootPath)` - generates cache path
- `ComputeProjectHash(path)` - deterministic hash for cache key

**Acceptance:**
- Creates `~/.cache/mcpsharp/` directory if needed
- Uses SHA256 hash of canonical path for filename
- Thread-safe connection handling
- Implements IAsyncDisposable

### 1.5 Create ProjectDatabase Service - File Operations
**File:** `src/MCPsharp/Services/Database/ProjectDatabase.Files.cs` (partial class)
**Task:** Implement file tracking operations
**Methods:**
- `UpsertFileAsync(relativePath, contentHash, sizeBytes, language)`
- `GetFileAsync(relativePath)`
- `GetStaleFilesAsync(currentHashes)` - files needing re-index
- `DeleteFileAsync(relativePath)`
- `GetAllFilesAsync()`

**Acceptance:**
- Efficient batch operations
- Proper parameterized queries (no SQL injection)
- Returns strongly-typed results

### 1.6 Create ProjectDatabase Service - Symbol Operations
**File:** `src/MCPsharp/Services/Database/ProjectDatabase.Symbols.cs` (partial class)
**Task:** Implement symbol CRUD operations
**Methods:**
- `UpsertSymbolAsync(symbol)`
- `UpsertSymbolsBatchAsync(symbols)` - bulk insert for efficiency
- `FindSymbolsByNameAsync(name, kind?, limit)`
- `GetSymbolsInFileAsync(fileId)`
- `DeleteSymbolsForFileAsync(fileId)`
- `SearchSymbolsAsync(query, kinds?, namespaces?)`

**Acceptance:**
- Batch insert uses transactions
- Full-text search on symbol names
- Filters work correctly

### 1.7 Create ProjectDatabase Service - Reference Operations
**File:** `src/MCPsharp/Services/Database/ProjectDatabase.References.cs` (partial class)
**Task:** Implement cross-reference operations
**Methods:**
- `UpsertReferenceAsync(reference)`
- `UpsertReferencesBatchAsync(references)`
- `FindCallersAsync(symbolId)` - who calls this
- `FindCalleesAsync(symbolId)` - what does this call
- `GetCallChainAsync(symbolId, direction, maxDepth)`
- `DeleteReferencesForFileAsync(fileId)`

**Acceptance:**
- Recursive CTE queries for call chains
- Direction enum (Forward/Backward)
- Depth limiting works

### 1.8 Create ProjectDatabase Unit Tests
**File:** `tests/MCPsharp.Tests/Services/Database/ProjectDatabaseTests.cs`
**Task:** Comprehensive unit tests
**Test cases:**
- Database creation in cache directory
- CRUD operations for each entity type
- Batch operations
- Stale file detection
- Call chain queries
- Concurrent access safety
- Hash collision handling

**Acceptance:**
- All tests pass
- Coverage > 80% for database service
- Tests use in-memory SQLite for speed

---

## Phase 2: MCP Resources for Overview

### 2.1 Create MCP Resource Models
**File:** `src/MCPsharp/Models/McpResource.cs`
**Task:** Define MCP resource protocol models
**Classes:**
- `McpResource` - name, uri, description, mimeType
- `McpResourceContent` - uri, mimeType, text/blob
- `ResourceListResult` - resources array
- `ResourceReadResult` - contents array

**Acceptance:**
- Matches MCP specification
- JSON serialization attributes correct

### 2.2 Create Resource Registry
**File:** `src/MCPsharp/Services/McpResourceRegistry.cs`
**Task:** Manage available MCP resources
**Methods:**
- `ListResources()` - returns available resources
- `ReadResource(uri)` - returns resource content
- `RegisterResource(resource, contentProvider)`
- `UnregisterResource(uri)`

**Resources to register:**
- `project://overview` - project summary markdown
- `project://structure` - file/folder structure
- `project://dependencies` - project dependencies
- `project://symbols` - top-level symbols summary
- `project://guidance` - usage best practices

**Acceptance:**
- Resources dynamically update based on project state
- Content providers are lazy-evaluated
- Thread-safe registration

### 2.3 Implement Resource Content Generators
**File:** `src/MCPsharp/Services/ResourceContentGenerators.cs`
**Task:** Generate content for each resource type
**Generators:**
- `GenerateOverviewContent()` - markdown with project name, solutions, projects, stats
- `GenerateStructureContent()` - tree view of project
- `GenerateDependenciesContent()` - NuGet and project refs
- `GenerateSymbolsSummaryContent()` - namespace/class counts
- `GenerateGuidanceContent()` - best practices for AI usage

**Acceptance:**
- Content is well-formatted markdown
- Pulls from SQLite cache where available
- Falls back gracefully if cache incomplete

### 2.4 Add Resource Handling to JsonRpcHandler
**File:** `src/MCPsharp/Services/JsonRpcHandler.cs`
**Task:** Handle `resources/list` and `resources/read` methods
**Changes:**
- Add `HandleResourcesList()` method
- Add `HandleResourcesRead(uri)` method
- Wire up in main dispatch switch
- Include resources capability in `initialize` response

**Acceptance:**
- Follows MCP protocol spec
- Returns proper JSON-RPC responses
- Error handling for unknown resources

### 2.5 Create MCP Resources Tests
**File:** `tests/MCPsharp.Tests/Services/McpResourceRegistryTests.cs`
**Task:** Test resource system
**Test cases:**
- Resource listing
- Resource reading
- Content generation accuracy
- Unknown resource error handling
- Dynamic resource updates

**Acceptance:**
- All tests pass
- JSON-RPC format validated

---

## Phase 3: Symbol Indexing & Cross-References

### 3.1 Create Symbol Indexer Service
**File:** `src/MCPsharp/Services/Indexing/SymbolIndexerService.cs`
**Task:** Extract symbols from Roslyn and store in SQLite
**Methods:**
- `IndexProjectAsync(compilation)` - full index
- `IndexFileAsync(document)` - single file
- `IndexFilesAsync(documents)` - batch with parallelism
- `GetIndexingProgress()` - for status reporting

**Acceptance:**
- Extracts classes, interfaces, structs, enums, methods, properties, fields
- Includes full signature information
- Reports progress for large projects
- Cancellation support

### 3.2 Create Reference Indexer Service
**File:** `src/MCPsharp/Services/Indexing/ReferenceIndexerService.cs`
**Task:** Build cross-reference graph
**Methods:**
- `IndexReferencesAsync(compilation)` - build full graph
- `IndexFileReferencesAsync(document)` - single file
- `GetReferencesProgress()` - status reporting

**Reference types to track:**
- Method calls
- Property access
- Type usage (inheritance, implements, field types)
- Constructor invocations

**Acceptance:**
- Correctly identifies all reference types
- Handles partial classes
- Handles extension methods

### 3.3 Integrate Indexing with Project Open
**File:** `src/MCPsharp/Services/ProjectContextManager.cs`
**Task:** Trigger indexing on project open
**Changes:**
- Open/create SQLite database on project open
- Check file hashes for staleness
- Queue background indexing for stale files
- Update resource content after indexing

**Acceptance:**
- Non-blocking - returns immediately
- Background indexing continues
- Progress queryable via resource

### 3.4 Optimize Existing Tools to Use Cache
**Files:**
- `src/MCPsharp/Services/McpToolRegistry.Symbols.cs`
- `src/MCPsharp/Services/Roslyn/SymbolQueryService.cs`
**Task:** Use SQLite cache for faster symbol queries
**Changes:**
- `find_symbol` checks cache first
- `find_references` uses cache when available
- `find_callers` / `find_call_chains` use cached graph
- Falls back to Roslyn if cache miss

**Acceptance:**
- Faster response times (measure before/after)
- Results match Roslyn-only implementation
- Cache invalidation on file change

### 3.5 Add Incremental Update Support
**File:** `src/MCPsharp/Services/Indexing/IncrementalIndexer.cs`
**Task:** Update index on file changes
**Methods:**
- `OnFileChanged(filePath)` - reindex single file
- `OnFileDeleted(filePath)` - remove from index
- `OnFileCreated(filePath)` - add to index

**Acceptance:**
- Efficient single-file updates
- Updates reference graph correctly
- File watcher integration ready

### 3.6 Create Indexing Tests
**File:** `tests/MCPsharp.Tests/Services/Indexing/SymbolIndexerServiceTests.cs`
**File:** `tests/MCPsharp.Tests/Services/Indexing/ReferenceIndexerServiceTests.cs`
**Task:** Test indexing accuracy
**Test cases:**
- All symbol types extracted
- Signatures correct
- References correctly identified
- Incremental updates work
- Cache-vs-Roslyn consistency

**Acceptance:**
- All tests pass
- Test projects with various C# features

---

## Phase 4: Multi-Solution Support

### 4.1 Enhance Solution Discovery
**File:** `src/MCPsharp/Services/SolutionDiscoveryService.cs` (new)
**Task:** Find and catalog all solutions
**Methods:**
- `DiscoverSolutionsAsync(rootPath)` - find all .sln files
- `DiscoverProjectsAsync(rootPath)` - find all .csproj files
- `BuildDependencyGraph(solutions)` - project relationships
- `GetUnifiedProjectList()` - deduplicated project list

**Acceptance:**
- Handles nested solutions
- Detects shared projects
- Returns unified view

### 4.2 Enhance RoslynWorkspace for Multi-Solution
**File:** `src/MCPsharp/Services/Roslyn/RoslynWorkspace.cs`
**Task:** Load multiple solutions into unified workspace
**Changes:**
- Load all discovered solutions
- Merge into single compilation context
- Handle duplicate project references
- Track which solution each project came from

**Acceptance:**
- Cross-solution references resolve
- No duplicate symbol warnings
- Memory efficient

### 4.3 Update ProjectContextManager for Multi-Solution
**File:** `src/MCPsharp/Services/ProjectContextManager.cs`
**Task:** Track multiple solutions
**Changes:**
- Store list of loaded solutions
- Enhanced `GetProjectInfo()` with solution breakdown
- `GetSolutionList()` method
- `GetProjectsInSolution(solutionPath)` method

**Acceptance:**
- Overview shows all solutions
- Per-solution stats available

### 4.4 Create Multi-Solution Tests
**File:** `tests/MCPsharp.Tests/Services/MultiSolutionTests.cs`
**Task:** Test multi-solution scenarios
**Test cases:**
- Monorepo with multiple solutions
- Nested solutions
- Cross-solution project references
- Duplicate project handling

**Acceptance:**
- All tests pass
- Uses realistic test fixtures

---

## Phase 5: UX & Documentation

### 5.1 Enhance Tool Descriptions
**File:** `src/MCPsharp/Services/McpToolRegistry.cs`
**Task:** Update tool descriptions to guide AI usage
**Changes for each tool:**
- Emphasize semantic queries over file reading
- Suggest when to use each tool
- Note that subagents should prefer MCP tools

**Example enhancement:**
```csharp
Description = @"Search for symbols by name using the semantic index.
PREFERRED over reading files directly - returns structured data.
Use this for finding classes, methods, properties by name.
Supports partial matching and kind filtering."
```

**Acceptance:**
- All tools have enhanced descriptions
- Descriptions guide toward MCP usage
- No marketing language, just guidance

### 5.2 Create Guidance MCP Prompt
**File:** `src/MCPsharp/Services/McpPromptRegistry.cs` (new)
**Task:** Implement MCP prompts for usage guidance
**Prompts to register:**
- `mcpsharp://analyze-codebase` - template for codebase analysis
- `mcpsharp://implement-feature` - template for feature implementation
- `mcpsharp://fix-bug` - template for bug fixing

**Each prompt contains:**
- Recommended tool sequence
- When to use semantic vs. file tools
- Best practices for large codebases

**Acceptance:**
- Prompts follow MCP spec
- Content is practical and helpful

### 5.3 Add Prompt Handling to JsonRpcHandler
**File:** `src/MCPsharp/Services/JsonRpcHandler.cs`
**Task:** Handle `prompts/list` and `prompts/get` methods
**Changes:**
- Add `HandlePromptsList()` method
- Add `HandlePromptsGet(name)` method
- Include prompts capability in `initialize` response

**Acceptance:**
- Follows MCP protocol spec
- Returns proper JSON-RPC responses

### 5.4 Create UX Enhancement Tests
**File:** `tests/MCPsharp.Tests/Services/McpPromptRegistryTests.cs`
**Task:** Test prompt system
**Test cases:**
- Prompt listing
- Prompt retrieval
- Prompt argument substitution

**Acceptance:**
- All tests pass

---

## Phase 6: Integration & Verification

### 6.1 Integration Test Suite
**File:** `tests/MCPsharp.Tests/Integration/SqliteCacheIntegrationTests.cs`
**Task:** End-to-end tests
**Test cases:**
- Full project open → index → query cycle
- Resource availability after open
- Cache persistence across restarts
- Multi-solution integration

### 6.2 Performance Benchmarks
**File:** `tests/MCPsharp.Tests/Performance/CachePerformanceTests.cs`
**Task:** Measure performance improvements
**Benchmarks:**
- Symbol lookup: cache vs. Roslyn
- Call chain query: cache vs. Roslyn
- Initial indexing time
- Incremental update time

### 6.3 Final Verification
**Task:** Verify all acceptance criteria
- All tests pass (`dotnet test`)
- No compiler warnings (`dotnet build --warnaserror`)
- Integration tests pass
- Performance benchmarks show improvement

---

## Subtask Dependency Graph

```
Phase 1: Database Foundation
  1.1 ──┬── 1.2 ── 1.3 ── 1.4 ──┬── 1.5
        │                       ├── 1.6
        │                       └── 1.7
        └── 1.8 (can start after 1.4)

Phase 2: MCP Resources (depends on 1.4)
  2.1 ── 2.2 ── 2.3 ── 2.4 ── 2.5

Phase 3: Indexing (depends on 1.5, 1.6, 1.7)
  3.1 ──┬── 3.3 ── 3.4
  3.2 ──┘
  3.5 (after 3.1, 3.2)
  3.6 (after 3.1, 3.2)

Phase 4: Multi-Solution (depends on 3.3)
  4.1 ── 4.2 ── 4.3 ── 4.4

Phase 5: UX (can run in parallel with Phase 3-4)
  5.1 (independent)
  5.2 ── 5.3 ── 5.4

Phase 6: Integration (after all)
  6.1 ── 6.2 ── 6.3
```

---

## Parallel Execution Strategy

**Wave 1 (can run in parallel):**
- 1.1 (NuGet dependency)
- 1.2 (Database models)
- 2.1 (Resource models)
- 5.1 (Tool descriptions - independent)

**Wave 2 (after Wave 1):**
- 1.3 (Schema - needs 1.2)
- 2.2 (Resource registry - needs 2.1)
- 5.2 (Prompt registry - independent)

**Wave 3 (after Wave 2):**
- 1.4 (Database core - needs 1.3)
- 2.3 (Content generators - needs 2.2)
- 5.3 (Prompt handler - needs 5.2)

**Wave 4 (after Wave 3):**
- 1.5, 1.6, 1.7 (Database operations - need 1.4, can run in parallel)
- 2.4 (Resource handler - needs 2.3)
- 5.4 (Prompt tests - needs 5.3)

**Wave 5 (after Wave 4):**
- 1.8 (Database tests - needs 1.5-1.7)
- 2.5 (Resource tests - needs 2.4)
- 3.1, 3.2 (Indexers - need 1.5-1.7, can run in parallel)

**Wave 6 (after Wave 5):**
- 3.3 (Integration - needs 3.1, 3.2)
- 3.6 (Indexing tests - needs 3.1, 3.2)

**Wave 7 (after Wave 6):**
- 3.4, 3.5 (Optimization, incremental - need 3.3)
- 4.1 (Solution discovery - needs 3.3)

**Wave 8 (after Wave 7):**
- 4.2 (Multi-solution workspace - needs 4.1)

**Wave 9 (after Wave 8):**
- 4.3 (Context manager update - needs 4.2)

**Wave 10 (after Wave 9):**
- 4.4 (Multi-solution tests - needs 4.3)

**Wave 11 (after all):**
- 6.1 (Integration tests)
- 6.2 (Performance benchmarks)
- 6.3 (Final verification)
