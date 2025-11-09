# MCPsharp Text Search Implementation Design
## ULTRATHINK Analysis & Architecture

---

## Executive Summary

This document provides a comprehensive design for implementing **text-based search capabilities** in MCPsharp, addressing the critical **Priority 1 gap** of missing regex/literal/comment search functionality. The design integrates seamlessly with existing architecture while providing fast, scalable, and flexible search across large codebases.

**Key Design Decisions:**
- **Hybrid Approach:** Combine Roslyn for code-aware search with optimized text scanning for general patterns
- **Cursor-Based Pagination:** Efficient memory management for large result sets
- **Streaming Architecture:** Progressive result delivery without memory exhaustion
- **Three-Tool Strategy:** Separate tools for different search needs (text/regex/files)

---

## 1. Architecture Overview

### 1.1 Component Hierarchy

```
┌─────────────────────────────────────────────────┐
│              MCP Tool Layer                      │
├─────────────────────────────────────────────────┤
│ • search_text    - Literal text search          │
│ • search_regex   - Regex pattern search         │
│ • search_files   - File path/name search        │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│           TextSearchService                      │
├─────────────────────────────────────────────────┤
│ • Search orchestration                          │
│ • Result pagination                             │
│ • Cache management                              │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│          Search Engine Layer                     │
├─────────────────────────────────────────────────┤
│ • RoslynSearchEngine    (AST-aware)            │
│ • RegexSearchEngine     (Pattern matching)      │
│ • IndexedSearchEngine   (Pre-built indices)     │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│         Infrastructure Layer                     │
├─────────────────────────────────────────────────┤
│ • FileEnumerator (from BulkEditService)        │
│ • StreamingFileProcessor (large files)          │
│ • ResponseProcessor (token limiting)            │
│ • ProgressTrackerService (monitoring)           │
└─────────────────────────────────────────────────┘
```

### 1.2 Design Philosophy

**Principle:** Layer search engines by speed and capability
1. **IndexedSearchEngine** - Fastest, uses pre-built indices for common patterns
2. **RegexSearchEngine** - Fast, direct file scanning with compiled regex
3. **RoslynSearchEngine** - Slower but semantic-aware (comments, strings, trivia)

---

## 2. Backend Implementation Strategy

### 2.1 Hybrid Search Engine Approach (Selected)

After analysis, the **Hybrid Approach** combining multiple engines provides the best balance:

#### Advantages:
- ✅ Optimal performance for different search types
- ✅ Semantic awareness when needed
- ✅ Fast literal search when AST isn't required
- ✅ Graceful degradation (fallback engines)

#### Implementation:

```csharp
public interface ISearchEngine
{
    Task<SearchEngineResult> SearchAsync(
        SearchContext context,
        CancellationToken ct);

    SearchEngineCapabilities Capabilities { get; }
    int Priority { get; } // For engine selection
}

public class SearchEngineCapabilities
{
    public bool SupportsRegex { get; init; }
    public bool SupportsSemanticSearch { get; init; }
    public bool SupportsIncrementalSearch { get; init; }
    public bool RequiresCompilation { get; init; }
    public int MaxFileSizeBytes { get; init; }
}
```

### 2.2 Engine Selection Matrix

| Search Type | Primary Engine | Fallback | Rationale |
|------------|---------------|----------|-----------|
| Comments | RoslynSearchEngine | RegexSearchEngine | SyntaxTrivia provides accurate comment detection |
| String Literals | RoslynSearchEngine | RegexSearchEngine | Semantic model identifies actual strings vs code |
| Arbitrary Text | RegexSearchEngine | - | Fastest for non-semantic patterns |
| TODO/FIXME | IndexedSearchEngine | RegexSearchEngine | Common patterns can be pre-indexed |
| File Paths | FileEnumerator | - | Already optimized in BulkEditService |

---

## 3. Pagination Strategy

### 3.1 Cursor-Based Pagination (Selected)

**Design Decision:** Use cursor-based pagination with result caching for stateless operation.

```csharp
public class SearchCursor
{
    public string SearchId { get; init; } // Unique search session
    public long LastFileOffset { get; init; } // File enumeration position
    public long LastMatchOffset { get; init; } // Match position in file
    public string LastFilePath { get; init; } // Resume point
    public DateTime ExpiresAt { get; init; } // Cache expiration
}

public class PaginatedSearchResult
{
    public SearchResult Result { get; init; }
    public SearchCursor? NextCursor { get; init; }
    public SearchStatistics Statistics { get; init; }
}
```

#### Implementation Details:

1. **Stateless Cursors:** Encode position in cursor token
2. **Result Caching:** 5-minute cache for active searches
3. **Progressive Loading:** Stream results as found
4. **Memory Bounds:** Max 1000 matches in memory at once

```csharp
// Cursor encoding (base64 JSON)
var cursor = Convert.ToBase64String(
    JsonSerializer.SerializeToUtf8Bytes(new SearchCursor
    {
        SearchId = Guid.NewGuid().ToString(),
        LastFileOffset = 1523,
        LastMatchOffset = 45678,
        LastFilePath = "/src/Services/Example.cs",
        ExpiresAt = DateTime.UtcNow.AddMinutes(5)
    }));
```

---

## 4. Search Scope & Filtering

### 4.1 Intelligent Scope Detection

```csharp
public class SearchScope
{
    public ScopeType Type { get; init; }
    public HashSet<string> IncludePatterns { get; init; }
    public HashSet<string> ExcludePatterns { get; init; }
    public bool SearchGeneratedCode { get; init; } = false;
    public bool SearchBinaryFiles { get; init; } = false;
    public long MaxFileSizeBytes { get; init; } = 10_000_000; // 10MB
}

public enum ScopeType
{
    SourceCode,      // *.cs, *.vb
    Configuration,   // *.json, *.xml, *.yaml
    Documentation,   // *.md, *.txt
    Everything,      // All text files
    Custom          // User-defined patterns
}
```

### 4.2 Default Exclusions

```csharp
private static readonly HashSet<string> DefaultExclusions = new()
{
    "**/bin/**",
    "**/obj/**",
    "**/node_modules/**",
    "**/.git/**",
    "**/packages/**",
    "**/*.dll",
    "**/*.exe",
    "**/*.pdb",
    "**/TestResults/**"
};
```

---

## 5. Performance Optimization

### 5.1 Parallel Processing Strategy

```csharp
public class ParallelSearchOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public int FileChunkSize { get; init; } = 50; // Files per batch
    public bool EnableProgressiveResults { get; init; } = true;
    public int ProgressUpdateIntervalMs { get; init; } = 100;
}

// Implementation
await Parallel.ForEachAsync(
    fileChunks,
    new ParallelOptions
    {
        MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
        CancellationToken = ct
    },
    async (chunk, ct) =>
    {
        var engine = SelectEngine(searchRequest);
        var results = await engine.SearchChunkAsync(chunk, ct);
        await resultAggregator.AddResultsAsync(results);
    });
```

### 5.2 Regex Compilation & Caching

```csharp
public class RegexCache
{
    private readonly MemoryCache _cache;
    private readonly RegexCacheOptions _options;

    public Regex GetOrCompile(string pattern, RegexOptions options)
    {
        var key = $"{pattern}:{options}";
        return _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            entry.Size = EstimateRegexSize(pattern);

            // Compile with timeout to prevent ReDoS
            return new Regex(pattern, options | RegexOptions.Compiled,
                TimeSpan.FromSeconds(1));
        });
    }
}
```

### 5.3 Streaming Results

```csharp
public async IAsyncEnumerable<SearchMatch> SearchStreamAsync(
    SearchRequest request,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var buffer = Channel.CreateUnbounded<SearchMatch>();

    // Producer task
    _ = Task.Run(async () =>
    {
        try
        {
            await foreach (var file in EnumerateFilesAsync(request, ct))
            {
                await foreach (var match in SearchFileAsync(file, request, ct))
                {
                    await buffer.Writer.WriteAsync(match, ct);
                }
            }
        }
        finally
        {
            buffer.Writer.Complete();
        }
    }, ct);

    // Consumer yields results immediately
    await foreach (var match in buffer.Reader.ReadAllAsync(ct))
    {
        yield return match;
    }
}
```

---

## 6. API Specification

### 6.1 MCP Tools

#### Tool: `search_text`
```json
{
  "name": "search_text",
  "description": "Search for literal text patterns across project files",
  "inputSchema": {
    "type": "object",
    "properties": {
      "pattern": {
        "type": "string",
        "description": "Text pattern to search for"
      },
      "caseSensitive": {
        "type": "boolean",
        "description": "Case-sensitive search (default: false)"
      },
      "wholeWord": {
        "type": "boolean",
        "description": "Match whole words only (default: false)"
      },
      "includePattern": {
        "type": "string",
        "description": "Glob pattern for files to include (e.g., '*.cs')"
      },
      "contextLines": {
        "type": "integer",
        "description": "Lines of context before/after match (default: 2)"
      },
      "maxResults": {
        "type": "integer",
        "description": "Maximum results to return (default: 100)"
      },
      "cursor": {
        "type": "string",
        "description": "Pagination cursor from previous search"
      }
    },
    "required": ["pattern"]
  }
}
```

#### Tool: `search_regex`
```json
{
  "name": "search_regex",
  "description": "Search using regular expressions with advanced pattern matching",
  "inputSchema": {
    "type": "object",
    "properties": {
      "pattern": {
        "type": "string",
        "description": "Regular expression pattern"
      },
      "multiline": {
        "type": "boolean",
        "description": "Enable multiline mode (default: false)"
      },
      "includePattern": {
        "type": "string",
        "description": "Glob pattern for files to include"
      },
      "excludePattern": {
        "type": "string",
        "description": "Glob pattern for files to exclude"
      },
      "maxResults": {
        "type": "integer",
        "description": "Maximum results to return (default: 100)"
      },
      "cursor": {
        "type": "string",
        "description": "Pagination cursor"
      }
    },
    "required": ["pattern"]
  }
}
```

#### Tool: `search_files`
```json
{
  "name": "search_files",
  "description": "Search for files by name or path pattern",
  "inputSchema": {
    "type": "object",
    "properties": {
      "pattern": {
        "type": "string",
        "description": "File name or path pattern (glob supported)"
      },
      "includeHidden": {
        "type": "boolean",
        "description": "Include hidden files (default: false)"
      },
      "includeDirectories": {
        "type": "boolean",
        "description": "Include directories in results (default: false)"
      },
      "maxResults": {
        "type": "integer",
        "description": "Maximum results to return (default: 1000)"
      }
    },
    "required": ["pattern"]
  }
}
```

### 6.2 Response Format

```typescript
interface SearchResponse {
  success: boolean;
  matches: SearchMatch[];
  statistics: {
    totalMatches: number;
    matchesReturned: number;
    filesSearched: number;
    filesWithMatches: number;
    searchDurationMs: number;
    engineUsed: string;
  };
  pagination?: {
    hasMore: boolean;
    nextCursor?: string;
    estimatedRemaining?: number;
  };
  warnings?: string[];
}

interface SearchMatch {
  file: {
    path: string;
    relativePath: string;
    size: number;
    lastModified: string;
  };
  match: {
    lineNumber: number;
    columnStart: number;
    columnEnd: number;
    matchText: string;
    lineContent: string;
  };
  context: {
    before: string[];
    after: string[];
  };
  metadata?: {
    symbolKind?: string; // If from Roslyn
    isComment?: boolean;
    isStringLiteral?: boolean;
  };
}
```

---

## 7. Implementation Plan

### 7.1 File Structure

```
src/MCPsharp/
├── Services/
│   ├── Search/
│   │   ├── ITextSearchService.cs
│   │   ├── TextSearchService.cs
│   │   ├── Engines/
│   │   │   ├── ISearchEngine.cs
│   │   │   ├── RegexSearchEngine.cs
│   │   │   ├── RoslynSearchEngine.cs
│   │   │   ├── IndexedSearchEngine.cs
│   │   │   └── SearchEngineSelector.cs
│   │   ├── Pagination/
│   │   │   ├── SearchCursorManager.cs
│   │   │   └── ResultCache.cs
│   │   └── Filters/
│   │       ├── FileFilterService.cs
│   │       └── ScopeResolver.cs
│   └── McpToolRegistry.Search.cs (partial class)
├── Models/
│   └── Search/
│       ├── SearchModels.cs (existing)
│       ├── SearchEngineModels.cs
│       └── PaginationModels.cs
└── Tests/
    └── Services/
        └── Search/
            ├── TextSearchServiceTests.cs
            ├── RegexSearchEngineTests.cs
            └── SearchPaginationTests.cs
```

### 7.2 Implementation Phases

#### Phase 1: Core Infrastructure (2-3 days)
- [ ] Create `ISearchEngine` interface and base implementation
- [ ] Implement `RegexSearchEngine` with basic file scanning
- [ ] Integrate with `FileOperationsService` for file enumeration
- [ ] Add basic result models and response formatting

#### Phase 2: Pagination & Streaming (2 days)
- [ ] Implement cursor-based pagination
- [ ] Add result caching with expiration
- [ ] Implement streaming result delivery
- [ ] Add progress tracking integration

#### Phase 3: Advanced Engines (3 days)
- [ ] Implement `RoslynSearchEngine` for semantic search
- [ ] Add `IndexedSearchEngine` for common patterns
- [ ] Implement `SearchEngineSelector` with fallback logic
- [ ] Add engine-specific optimizations

#### Phase 4: MCP Tool Integration (1 day)
- [ ] Register tools in `McpToolRegistry`
- [ ] Implement tool handlers with validation
- [ ] Add ResponseProcessor integration
- [ ] Test with Claude Code

#### Phase 5: Performance & Polish (2 days)
- [ ] Add parallel processing optimizations
- [ ] Implement regex compilation caching
- [ ] Add comprehensive error handling
- [ ] Performance benchmarking and tuning

---

## 8. Performance Benchmarks

### 8.1 Target Metrics

| Metric | Target | Acceptable | Current (Baseline) |
|--------|--------|------------|-------------------|
| 500-file search | <2s | <5s | N/A |
| 10K-file search | <10s | <20s | N/A |
| Regex compilation | <50ms | <100ms | N/A |
| First result latency | <500ms | <1s | N/A |
| Memory usage (10K matches) | <100MB | <200MB | N/A |
| Concurrent searches | 10 | 5 | N/A |

### 8.2 Benchmark Implementation

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class TextSearchBenchmarks
{
    private TextSearchService _searchService;
    private SearchRequest _literalRequest;
    private SearchRequest _regexRequest;

    [GlobalSetup]
    public void Setup()
    {
        _searchService = new TextSearchService(/* deps */);
        _literalRequest = new SearchRequest
        {
            Pattern = "TODO",
            MaxResults = 100
        };
        _regexRequest = new SearchRequest
        {
            Pattern = @"\bTODO\b.*\n",
            Regex = true
        };
    }

    [Benchmark]
    public async Task SearchLiteral500Files()
        => await _searchService.SearchTextAsync(_literalRequest);

    [Benchmark]
    public async Task SearchRegex500Files()
        => await _searchService.SearchTextAsync(_regexRequest);
}
```

---

## 9. Integration Strategy

### 9.1 Existing Service Integration

| Service | Integration Point | Purpose |
|---------|------------------|---------|
| BulkEditService | File enumeration, exclusion patterns | Reuse optimized file discovery |
| StreamingFileProcessor | Large file handling | Stream search in files >10MB |
| ResponseProcessor | Token limiting | Respect MCP response limits |
| RoslynWorkspace | AST access | Semantic search in comments/strings |
| ProgressTrackerService | Progress reporting | Real-time search progress |

### 9.2 Code Example: Service Integration

```csharp
public class TextSearchService : ITextSearchService
{
    private readonly IFileOperationsService _fileOps;
    private readonly IStreamingFileProcessor _streamProcessor;
    private readonly ResponseProcessor _responseProcessor;
    private readonly RoslynWorkspace _workspace;
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<TextSearchService> _logger;

    public async Task<ProcessedResponse> SearchTextAsync(
        SearchRequest request,
        CancellationToken ct)
    {
        // Start progress tracking
        var operationId = await _progressTracker.StartOperationAsync(
            "text_search",
            request.Pattern);

        try
        {
            // Get files using existing infrastructure
            var files = await _fileOps.EnumerateFilesAsync(
                request.TargetPath ?? _workspace.CurrentProject,
                request.IncludePattern,
                request.ExcludePattern,
                ct);

            // Select appropriate engine
            var engine = SelectSearchEngine(request);

            // Execute search with streaming
            var results = new List<SearchMatch>();
            await foreach (var match in engine.SearchStreamAsync(
                files, request, ct).WithCancellation(ct))
            {
                results.Add(match);

                // Update progress
                await _progressTracker.UpdateProgressAsync(
                    operationId,
                    results.Count);

                // Check limits
                if (results.Count >= request.MaxResults)
                    break;
            }

            // Build response
            var response = BuildSearchResponse(results, request);

            // Apply token limiting
            return _responseProcessor.ProcessResponse(
                response,
                "search_text");
        }
        finally
        {
            await _progressTracker.CompleteOperationAsync(operationId);
        }
    }
}
```

---

## 10. Error Handling & Edge Cases

### 10.1 ReDoS Prevention

```csharp
public class SafeRegexValidator
{
    private static readonly string[] DangerousPatterns =
    {
        @"(\w+)*",     // Exponential backtracking
        @"(a+)+",      // Nested quantifiers
        @"(.*)*",      // Catastrophic backtracking
    };

    public ValidationResult ValidatePattern(string pattern)
    {
        // Check for dangerous patterns
        foreach (var dangerous in DangerousPatterns)
        {
            if (Regex.IsMatch(pattern, dangerous))
            {
                return ValidationResult.Dangerous(
                    "Pattern may cause performance issues");
            }
        }

        // Test with timeout
        try
        {
            var testRegex = new Regex(pattern,
                RegexOptions.None,
                TimeSpan.FromMilliseconds(100));
            testRegex.IsMatch("test string");
            return ValidationResult.Success();
        }
        catch (RegexMatchTimeoutException)
        {
            return ValidationResult.Dangerous(
                "Pattern execution timeout");
        }
    }
}
```

### 10.2 Binary File Detection

```csharp
public static class BinaryFileDetector
{
    public static async Task<bool> IsBinaryFileAsync(
        string filePath,
        CancellationToken ct)
    {
        const int sampleSize = 8192;
        var buffer = new byte[sampleSize];

        await using var stream = File.OpenRead(filePath);
        var bytesRead = await stream.ReadAsync(buffer, ct);

        // Check for null bytes (common in binary files)
        var nullBytes = 0;
        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0) nullBytes++;
        }

        // If >30% null bytes, likely binary
        return (double)nullBytes / bytesRead > 0.3;
    }
}
```

---

## 11. Future Extensibility

### 11.1 Planned Enhancements

1. **Lucene.NET Integration** - Full-text indexing for instant searches
2. **Incremental Indexing** - Background index updates on file changes
3. **Semantic Search** - "Find all error handling code" using ML
4. **Search Templates** - Predefined searches (TODOs, FIXMEs, etc.)
5. **Search History** - Recent searches with replay
6. **Distributed Search** - Multi-machine search for massive codebases

### 11.2 Extension Points

```csharp
public interface ISearchEnginePlugin
{
    string Name { get; }
    Task<bool> CanHandleAsync(SearchRequest request);
    IAsyncEnumerable<SearchMatch> SearchAsync(
        SearchContext context,
        CancellationToken ct);
}

public class SearchEngineRegistry
{
    private readonly List<ISearchEnginePlugin> _plugins = new();

    public void RegisterPlugin(ISearchEnginePlugin plugin)
    {
        _plugins.Add(plugin);
        _plugins.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }
}
```

---

## 12. Testing Strategy

### 12.1 Test Coverage Requirements

- Unit tests: 80% coverage minimum
- Integration tests: Key scenarios
- Performance tests: Benchmark suite
- Stress tests: Large file handling

### 12.2 Test Data Generation

```csharp
public class SearchTestDataGenerator
{
    public static async Task GenerateTestProjectAsync(string path)
    {
        // Generate varied file types
        var generators = new ITestFileGenerator[]
        {
            new CSharpFileGenerator(),      // .cs with TODOs, comments
            new JsonConfigGenerator(),       // .json configs
            new MarkdownDocGenerator(),      // .md documentation
            new LargeFileGenerator(),        // 10MB+ files
            new BinaryFileGenerator()        // Binary files to skip
        };

        foreach (var gen in generators)
        {
            await gen.GenerateFilesAsync(path, count: 100);
        }
    }
}
```

---

## Conclusion

This design provides a comprehensive, performant, and extensible text search capability for MCPsharp that:

1. **Addresses the critical gap** of missing text/regex search
2. **Integrates seamlessly** with existing architecture
3. **Scales efficiently** to large codebases
4. **Provides flexible** search modes and options
5. **Maintains performance** through streaming and pagination
6. **Ensures safety** through ReDoS prevention and validation

The implementation can be completed in approximately **10-12 days** with the phased approach, delivering immediate value after Phase 1 (basic search) while building toward advanced capabilities.

**Next Steps:**
1. Review and approve design
2. Begin Phase 1 implementation
3. Create integration tests with sample projects
4. Deploy and gather performance metrics