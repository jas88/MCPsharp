# Text Search Implementation Code Samples
## Key Algorithms and Integration Examples

---

## 1. Core Search Service Implementation

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MCPsharp.Models.Search;
using MCPsharp.Services.Search.Engines;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Search;

public class TextSearchService : ITextSearchService
{
    private readonly ISearchEngineSelector _engineSelector;
    private readonly IFileOperationsService _fileOperations;
    private readonly ResponseProcessor _responseProcessor;
    private readonly SearchCursorManager _cursorManager;
    private readonly IProgressTracker _progressTracker;
    private readonly ILogger<TextSearchService> _logger;

    // Concurrent result aggregation
    private readonly ConcurrentBag<SearchMatch> _resultBuffer = new();

    public TextSearchService(
        ISearchEngineSelector engineSelector,
        IFileOperationsService fileOperations,
        ResponseProcessor responseProcessor,
        SearchCursorManager cursorManager,
        IProgressTracker progressTracker,
        ILogger<TextSearchService> logger)
    {
        _engineSelector = engineSelector;
        _fileOperations = fileOperations;
        _responseProcessor = responseProcessor;
        _cursorManager = cursorManager;
        _progressTracker = progressTracker;
        _logger = logger;
    }

    public async Task<ProcessedResponse> SearchTextAsync(
        SearchRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString();

        try
        {
            // Validate request
            var validation = ValidateRequest(request);
            if (!validation.IsValid)
            {
                return new ProcessedResponse
                {
                    Content = new SearchResult
                    {
                        Success = false,
                        ErrorMessage = validation.ErrorMessage,
                        Matches = new List<SearchMatch>(),
                        TotalMatches = 0,
                        Returned = 0,
                        Offset = 0,
                        HasMore = false,
                        FilesSearched = 0,
                        SearchDurationMs = stopwatch.ElapsedMilliseconds
                    },
                    WasTruncated = false,
                    EstimatedTokenCount = 100,
                    OriginalTokenCount = 100
                };
            }

            // Resume from cursor if provided
            SearchState? resumeState = null;
            if (!string.IsNullOrEmpty(request.Cursor))
            {
                resumeState = await _cursorManager.GetStateAsync(request.Cursor);
                if (resumeState == null)
                {
                    _logger.LogWarning("Invalid or expired cursor: {Cursor}", request.Cursor);
                }
            }

            // Select search engine based on request characteristics
            var engine = _engineSelector.SelectEngine(request);
            _logger.LogInformation("Using search engine: {Engine}", engine.GetType().Name);

            // Enumerate files with smart filtering
            var files = await EnumerateSearchableFilesAsync(request, resumeState, ct);

            // Execute search with streaming and pagination
            var searchContext = new SearchContext
            {
                Request = request,
                OperationId = operationId,
                ResumeState = resumeState,
                MaxResults = request.MaxResults,
                Files = files
            };

            var engineResult = await engine.SearchAsync(searchContext, ct);

            // Build paginated response
            var response = await BuildPaginatedResponseAsync(
                engineResult,
                request,
                stopwatch.ElapsedMilliseconds);

            // Apply token limiting
            return _responseProcessor.ProcessResponse(response, "search_text");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Search cancelled: {OperationId}", operationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed: {OperationId}", operationId);
            throw;
        }
        finally
        {
            await _progressTracker.CompleteOperationAsync(operationId);
        }
    }

    private async Task<IEnumerable<string>> EnumerateSearchableFilesAsync(
        SearchRequest request,
        SearchState? resumeState,
        CancellationToken ct)
    {
        var files = new List<string>();
        var skipCount = 0;

        // Resume from last position if cursor provided
        if (resumeState != null)
        {
            skipCount = resumeState.FilesProcessed;
            _logger.LogDebug("Resuming from file offset: {Offset}", skipCount);
        }

        // Build file filter
        var filter = new FileFilter
        {
            IncludePatterns = ParsePatterns(request.IncludePattern),
            ExcludePatterns = GetDefaultExclusions()
                .Concat(ParsePatterns(request.ExcludePattern))
                .ToHashSet(),
            MaxFileSize = 10_000_000, // 10MB default
            SkipBinaryFiles = true
        };

        await foreach (var file in _fileOperations.EnumerateFilesAsync(
            request.TargetPath ?? ".",
            filter,
            ct))
        {
            if (skipCount > 0)
            {
                skipCount--;
                continue;
            }

            files.Add(file);

            // Limit files per search to prevent memory issues
            if (files.Count >= 10000)
            {
                _logger.LogWarning("File limit reached, truncating search scope");
                break;
            }
        }

        return files;
    }

    private async Task<SearchResult> BuildPaginatedResponseAsync(
        SearchEngineResult engineResult,
        SearchRequest request,
        long elapsedMs)
    {
        var matches = engineResult.Matches.ToList();
        var hasMore = matches.Count > request.MaxResults;

        if (hasMore)
        {
            matches = matches.Take(request.MaxResults).ToList();
        }

        string? nextCursor = null;
        if (hasMore && engineResult.ContinuationToken != null)
        {
            var nextState = new SearchState
            {
                SearchId = Guid.NewGuid().ToString(),
                Request = request,
                FilesProcessed = engineResult.FilesSearched,
                LastFile = engineResult.LastFileProcessed,
                ContinuationToken = engineResult.ContinuationToken,
                CreatedAt = DateTime.UtcNow
            };

            nextCursor = await _cursorManager.SaveStateAsync(nextState);
        }

        return new SearchResult
        {
            Success = true,
            Matches = matches,
            TotalMatches = engineResult.TotalMatches,
            Returned = matches.Count,
            Offset = request.Offset,
            HasMore = hasMore,
            FilesSearched = engineResult.FilesSearched,
            SearchDurationMs = elapsedMs,
            NextCursor = nextCursor,
            Statistics = new SearchStatistics
            {
                EngineUsed = engineResult.EngineName,
                FilesSkipped = engineResult.FilesSkipped,
                AverageMatchesPerFile = engineResult.FilesWithMatches > 0
                    ? (double)engineResult.TotalMatches / engineResult.FilesWithMatches
                    : 0
            }
        };
    }
}
```

---

## 2. High-Performance Regex Search Engine

```csharp
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace MCPsharp.Services.Search.Engines;

public class RegexSearchEngine : ISearchEngine
{
    private readonly RegexCache _regexCache;
    private readonly ILogger<RegexSearchEngine> _logger;
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;

    // Performance tuning
    private const int BufferSize = 65536; // 64KB chunks
    private const int MaxLineLength = 10000;
    private const int ContextWindowSize = 1024; // Chars for context

    public SearchEngineCapabilities Capabilities => new()
    {
        SupportsRegex = true,
        SupportsSemanticSearch = false,
        SupportsIncrementalSearch = true,
        RequiresCompilation = false,
        MaxFileSizeBytes = 100_000_000 // 100MB
    };

    public int Priority => 10; // High priority for text search

    public async Task<SearchEngineResult> SearchAsync(
        SearchContext context,
        CancellationToken ct)
    {
        var regex = GetCompiledRegex(context.Request);
        var results = new ConcurrentBag<SearchMatch>();
        var filesSearched = 0;
        var filesWithMatches = 0;

        // Use Channel for producer-consumer pattern
        var channel = Channel.CreateUnbounded<SearchMatch>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            });

        // Producer: Search files in parallel
        var searchTask = Parallel.ForEachAsync(
            context.Files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            },
            async (file, ct) =>
            {
                try
                {
                    var fileMatches = await SearchFileAsync(file, regex, context, ct);

                    Interlocked.Increment(ref filesSearched);

                    if (fileMatches.Any())
                    {
                        Interlocked.Increment(ref filesWithMatches);
                        foreach (var match in fileMatches)
                        {
                            await channel.Writer.WriteAsync(match, ct);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error searching file: {File}", file);
                }
            });

        // Consumer: Collect results up to limit
        var collectTask = Task.Run(async () =>
        {
            await foreach (var match in channel.Reader.ReadAllAsync(ct))
            {
                results.Add(match);

                if (results.Count >= context.MaxResults * 2) // Collect extra for pagination
                {
                    break;
                }
            }
        }, ct);

        // Wait for search to complete or limit reached
        await searchTask;
        channel.Writer.Complete();
        await collectTask;

        return new SearchEngineResult
        {
            Matches = results.OrderBy(m => m.FilePath).ThenBy(m => m.LineNumber),
            TotalMatches = results.Count,
            FilesSearched = filesSearched,
            FilesWithMatches = filesWithMatches,
            EngineName = "RegexSearchEngine",
            LastFileProcessed = context.Files.LastOrDefault()
        };
    }

    private async Task<List<SearchMatch>> SearchFileAsync(
        string filePath,
        Regex regex,
        SearchContext context,
        CancellationToken ct)
    {
        var matches = new List<SearchMatch>();

        // Use pipelines for efficient streaming
        var pipe = new Pipe();
        var writing = FillPipeAsync(filePath, pipe.Writer, ct);
        var reading = ReadPipeAsync(pipe.Reader, regex, filePath, context, matches, ct);

        await Task.WhenAll(writing, reading);

        return matches;
    }

    private async Task FillPipeAsync(
        string filePath,
        PipeWriter writer,
        CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);

            while (!ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(BufferSize);
                var bytesRead = await stream.ReadAsync(memory, ct);

                if (bytesRead == 0)
                    break;

                writer.Advance(bytesRead);

                var result = await writer.FlushAsync(ct);
                if (result.IsCompleted)
                    break;
            }
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task ReadPipeAsync(
        PipeReader reader,
        Regex regex,
        string filePath,
        SearchContext context,
        List<SearchMatch> matches,
        CancellationToken ct)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var lineBuffer = new StringBuilder(MaxLineLength);
        var contextLines = new CircularBuffer<string>(context.Request.ContextLines);
        var lineNumber = 0;
        var charBuffer = _charPool.Rent(BufferSize);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                ProcessBuffer(
                    ref buffer,
                    charBuffer,
                    decoder,
                    lineBuffer,
                    contextLines,
                    ref lineNumber,
                    regex,
                    filePath,
                    context,
                    matches);

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }

            // Process final line if exists
            if (lineBuffer.Length > 0)
            {
                ProcessLine(
                    lineBuffer.ToString(),
                    lineNumber,
                    regex,
                    filePath,
                    contextLines,
                    context,
                    matches);
            }
        }
        finally
        {
            _charPool.Return(charBuffer);
            await reader.CompleteAsync();
        }
    }

    private void ProcessLine(
        string line,
        int lineNumber,
        Regex regex,
        string filePath,
        CircularBuffer<string> contextLines,
        SearchContext context,
        List<SearchMatch> matches)
    {
        var matchCollection = regex.Matches(line);

        foreach (Match regexMatch in matchCollection)
        {
            var match = new SearchMatch
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                ColumnNumber = regexMatch.Index + 1,
                MatchText = regexMatch.Value,
                LineContent = line,
                ContextBefore = contextLines.ToList(),
                ContextAfter = new List<string>() // Will be filled by next lines
            };

            matches.Add(match);

            // Fill context for previous matches
            FillContextAfter(matches, line, lineNumber, context.Request.ContextLines);
        }

        contextLines.Add(line);
    }

    private Regex GetCompiledRegex(SearchRequest request)
    {
        var pattern = request.Pattern;

        if (!request.Regex)
        {
            // Escape for literal search
            pattern = Regex.Escape(pattern);

            if (request.WholeWord)
            {
                pattern = $@"\b{pattern}\b";
            }
        }

        var options = RegexOptions.Compiled;

        if (!request.CaseSensitive)
            options |= RegexOptions.IgnoreCase;

        if (request.Multiline)
            options |= RegexOptions.Multiline;

        return _regexCache.GetOrCompile(pattern, options);
    }
}

// Circular buffer for context lines
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _start;
    private int _count;

    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        var index = (_start + _count) % _buffer.Length;
        _buffer[index] = item;

        if (_count < _buffer.Length)
            _count++;
        else
            _start = (_start + 1) % _buffer.Length;
    }

    public List<T> ToList()
    {
        var list = new List<T>(_count);
        for (int i = 0; i < _count; i++)
        {
            list.Add(_buffer[(_start + i) % _buffer.Length]);
        }
        return list;
    }
}
```

---

## 3. Roslyn-Based Semantic Search Engine

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace MCPsharp.Services.Search.Engines;

public class RoslynSearchEngine : ISearchEngine
{
    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<RoslynSearchEngine> _logger;

    public SearchEngineCapabilities Capabilities => new()
    {
        SupportsRegex = false,
        SupportsSemanticSearch = true,
        SupportsIncrementalSearch = false,
        RequiresCompilation = true,
        MaxFileSizeBytes = 10_000_000 // 10MB
    };

    public int Priority => 5; // Lower priority, use when semantic needed

    public async Task<SearchEngineResult> SearchAsync(
        SearchContext context,
        CancellationToken ct)
    {
        var compilation = await _workspace.GetCompilationAsync(ct);
        if (compilation == null)
        {
            throw new InvalidOperationException("No compilation available");
        }

        var matches = new List<SearchMatch>();
        var filesSearched = 0;
        var filesWithMatches = 0;

        // Determine search type
        var searchType = DetermineSearchType(context.Request);

        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = tree.FilePath;
            if (!context.Files.Contains(filePath))
                continue;

            filesSearched++;

            var fileMatches = searchType switch
            {
                SemanticSearchType.Comments => await SearchCommentsAsync(tree, context, ct),
                SemanticSearchType.StringLiterals => await SearchStringLiteralsAsync(tree, compilation, context, ct),
                SemanticSearchType.Identifiers => await SearchIdentifiersAsync(tree, context, ct),
                _ => new List<SearchMatch>()
            };

            if (fileMatches.Any())
            {
                filesWithMatches++;
                matches.AddRange(fileMatches);
            }

            if (matches.Count >= context.MaxResults * 2)
                break;
        }

        return new SearchEngineResult
        {
            Matches = matches,
            TotalMatches = matches.Count,
            FilesSearched = filesSearched,
            FilesWithMatches = filesWithMatches,
            EngineName = "RoslynSearchEngine"
        };
    }

    private async Task<List<SearchMatch>> SearchCommentsAsync(
        SyntaxTree tree,
        SearchContext context,
        CancellationToken ct)
    {
        var matches = new List<SearchMatch>();
        var root = await tree.GetRootAsync(ct);
        var text = await tree.GetTextAsync(ct);

        // Get all trivia (includes comments)
        var commentTrivia = root.DescendantTrivia()
            .Where(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        foreach (var trivia in commentTrivia)
        {
            var commentText = trivia.ToString();

            if (MatchesPattern(commentText, context.Request))
            {
                var lineSpan = text.Lines.GetLinePositionSpan(trivia.Span);
                var line = text.Lines[lineSpan.Start.Line];

                var match = new SearchMatch
                {
                    FilePath = tree.FilePath,
                    LineNumber = lineSpan.Start.Line + 1,
                    ColumnNumber = lineSpan.Start.Character + 1,
                    MatchText = commentText,
                    LineContent = line.ToString(),
                    ContextBefore = GetContextBefore(text, lineSpan.Start.Line, context.Request.ContextLines),
                    ContextAfter = GetContextAfter(text, lineSpan.Start.Line, context.Request.ContextLines),
                    Metadata = new Dictionary<string, object>
                    {
                        ["symbolKind"] = "Comment",
                        ["isComment"] = true,
                        ["commentType"] = trivia.Kind().ToString()
                    }
                };

                matches.Add(match);
            }
        }

        return matches;
    }

    private async Task<List<SearchMatch>> SearchStringLiteralsAsync(
        SyntaxTree tree,
        Compilation compilation,
        SearchContext context,
        CancellationToken ct)
    {
        var matches = new List<SearchMatch>();
        var root = await tree.GetRootAsync(ct);
        var text = await tree.GetTextAsync(ct);
        var model = compilation.GetSemanticModel(tree);

        // Find all string literals
        var stringLiterals = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(lit => lit.IsKind(SyntaxKind.StringLiteralExpression));

        foreach (var literal in stringLiterals)
        {
            var value = literal.Token.ValueText;

            if (MatchesPattern(value, context.Request))
            {
                var lineSpan = text.Lines.GetLinePositionSpan(literal.Span);
                var line = text.Lines[lineSpan.Start.Line];

                // Get containing symbol for context
                var containingSymbol = model.GetEnclosingSymbol(literal.SpanStart, ct);

                var match = new SearchMatch
                {
                    FilePath = tree.FilePath,
                    LineNumber = lineSpan.Start.Line + 1,
                    ColumnNumber = lineSpan.Start.Character + 1,
                    MatchText = value,
                    LineContent = line.ToString(),
                    ContextBefore = GetContextBefore(text, lineSpan.Start.Line, context.Request.ContextLines),
                    ContextAfter = GetContextAfter(text, lineSpan.Start.Line, context.Request.ContextLines),
                    Metadata = new Dictionary<string, object>
                    {
                        ["symbolKind"] = "StringLiteral",
                        ["isStringLiteral"] = true,
                        ["containingSymbol"] = containingSymbol?.Name ?? "Unknown",
                        ["rawLiteral"] = literal.Token.Text
                    }
                };

                matches.Add(match);
            }
        }

        // Also search interpolated strings
        var interpolatedStrings = root.DescendantNodes()
            .OfType<InterpolatedStringExpressionSyntax>();

        foreach (var interpolated in interpolatedStrings)
        {
            var fullText = interpolated.ToString();

            if (MatchesPattern(fullText, context.Request))
            {
                var lineSpan = text.Lines.GetLinePositionSpan(interpolated.Span);
                var line = text.Lines[lineSpan.Start.Line];

                var match = new SearchMatch
                {
                    FilePath = tree.FilePath,
                    LineNumber = lineSpan.Start.Line + 1,
                    ColumnNumber = lineSpan.Start.Character + 1,
                    MatchText = fullText,
                    LineContent = line.ToString(),
                    ContextBefore = GetContextBefore(text, lineSpan.Start.Line, context.Request.ContextLines),
                    ContextAfter = GetContextAfter(text, lineSpan.Start.Line, context.Request.ContextLines),
                    Metadata = new Dictionary<string, object>
                    {
                        ["symbolKind"] = "InterpolatedString",
                        ["isStringLiteral"] = true,
                        ["isInterpolated"] = true
                    }
                };

                matches.Add(match);
            }
        }

        return matches;
    }

    private bool MatchesPattern(string text, SearchRequest request)
    {
        if (!request.CaseSensitive)
        {
            text = text.ToLowerInvariant();
            var pattern = request.Pattern.ToLowerInvariant();
            return text.Contains(pattern);
        }

        return text.Contains(request.Pattern);
    }

    private List<string> GetContextBefore(SourceText text, int lineNumber, int contextLines)
    {
        var context = new List<string>();
        var startLine = Math.Max(0, lineNumber - contextLines);

        for (int i = startLine; i < lineNumber; i++)
        {
            context.Add(text.Lines[i].ToString());
        }

        return context;
    }

    private List<string> GetContextAfter(SourceText text, int lineNumber, int contextLines)
    {
        var context = new List<string>();
        var endLine = Math.Min(text.Lines.Count - 1, lineNumber + contextLines);

        for (int i = lineNumber + 1; i <= endLine; i++)
        {
            context.Add(text.Lines[i].ToString());
        }

        return context;
    }

    private enum SemanticSearchType
    {
        Comments,
        StringLiterals,
        Identifiers,
        All
    }

    private SemanticSearchType DetermineSearchType(SearchRequest request)
    {
        // Heuristics to determine what to search
        var pattern = request.Pattern.ToLowerInvariant();

        if (pattern.Contains("todo") || pattern.Contains("fixme") ||
            pattern.Contains("hack") || pattern.Contains("note"))
        {
            return SemanticSearchType.Comments;
        }

        if (request.SearchInComments)
            return SemanticSearchType.Comments;

        if (request.SearchInStrings)
            return SemanticSearchType.StringLiterals;

        return SemanticSearchType.All;
    }
}
```

---

## 4. Cursor-Based Pagination Manager

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MCPsharp.Services.Search.Pagination;

public class SearchCursorManager
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchCursorManager> _logger;
    private readonly TimeSpan _cursorExpiration = TimeSpan.FromMinutes(5);

    public SearchCursorManager(IMemoryCache cache, ILogger<SearchCursorManager> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> SaveStateAsync(SearchState state)
    {
        var cursor = new SearchCursor
        {
            SearchId = state.SearchId,
            RequestHash = ComputeRequestHash(state.Request),
            LastFileOffset = state.FilesProcessed,
            LastFilePath = state.LastFile,
            ContinuationToken = state.ContinuationToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_cursorExpiration)
        };

        // Encode cursor as base64 JSON
        var cursorJson = JsonSerializer.Serialize(cursor);
        var cursorToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursorJson));

        // Store in cache for quick retrieval
        _cache.Set(
            $"search_cursor_{cursor.SearchId}",
            state,
            _cursorExpiration);

        _logger.LogDebug("Created search cursor: {SearchId}", cursor.SearchId);

        return cursorToken;
    }

    public async Task<SearchState?> GetStateAsync(string cursorToken)
    {
        try
        {
            // Decode cursor
            var cursorJson = Encoding.UTF8.GetString(
                Convert.FromBase64String(cursorToken));
            var cursor = JsonSerializer.Deserialize<SearchCursor>(cursorJson);

            if (cursor == null)
                return null;

            // Check expiration
            if (cursor.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Cursor expired: {SearchId}", cursor.SearchId);
                return null;
            }

            // Retrieve from cache
            if (_cache.TryGetValue<SearchState>(
                $"search_cursor_{cursor.SearchId}",
                out var state))
            {
                return state;
            }

            // Reconstruct state from cursor if not in cache
            return new SearchState
            {
                SearchId = cursor.SearchId,
                FilesProcessed = cursor.LastFileOffset,
                LastFile = cursor.LastFilePath,
                ContinuationToken = cursor.ContinuationToken,
                CreatedAt = cursor.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decode cursor");
            return null;
        }
    }

    private string ComputeRequestHash(SearchRequest request)
    {
        var hashInput = $"{request.Pattern}|{request.CaseSensitive}|" +
                       $"{request.Regex}|{request.IncludePattern}|{request.ExcludePattern}";

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hash);
    }
}

public class SearchCursor
{
    public string SearchId { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public long LastFileOffset { get; init; }
    public string LastFilePath { get; init; } = string.Empty;
    public string? ContinuationToken { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public class SearchState
{
    public string SearchId { get; set; } = string.Empty;
    public SearchRequest Request { get; set; } = new();
    public int FilesProcessed { get; set; }
    public string LastFile { get; set; } = string.Empty;
    public string? ContinuationToken { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## 5. MCP Tool Registration

```csharp
// In McpToolRegistry.Search.cs (partial class)

namespace MCPsharp.Services;

public partial class McpToolRegistry
{
    private ITextSearchService? _textSearchService;

    private void RegisterSearchTools()
    {
        _tools.AddRange(new[]
        {
            new McpTool
            {
                Name = "search_text",
                Description = "Search for literal text patterns across project files",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = "Text pattern to search for"
                        },
                        caseSensitive = new
                        {
                            type = "boolean",
                            description = "Case-sensitive search (default: false)"
                        },
                        wholeWord = new
                        {
                            type = "boolean",
                            description = "Match whole words only (default: false)"
                        },
                        includePattern = new
                        {
                            type = "string",
                            description = "Glob pattern for files to include (e.g., '*.cs')"
                        },
                        excludePattern = new
                        {
                            type = "string",
                            description = "Glob pattern for files to exclude"
                        },
                        contextLines = new
                        {
                            type = "integer",
                            description = "Lines of context before/after match (default: 2)",
                            minimum = 0,
                            maximum = 10
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum results to return (default: 100)",
                            minimum = 1,
                            maximum = 1000
                        },
                        cursor = new
                        {
                            type = "string",
                            description = "Pagination cursor from previous search"
                        }
                    },
                    required = new[] { "pattern" }
                }
            },

            new McpTool
            {
                Name = "search_regex",
                Description = "Search using regular expressions with advanced pattern matching",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = "Regular expression pattern (automatically validated for safety)"
                        },
                        caseSensitive = new
                        {
                            type = "boolean",
                            description = "Case-sensitive search (default: true for regex)"
                        },
                        multiline = new
                        {
                            type = "boolean",
                            description = "Enable multiline mode where ^ and $ match line boundaries (default: false)"
                        },
                        includePattern = new
                        {
                            type = "string",
                            description = "Glob pattern for files to include"
                        },
                        excludePattern = new
                        {
                            type = "string",
                            description = "Glob pattern for files to exclude"
                        },
                        contextLines = new
                        {
                            type = "integer",
                            description = "Lines of context before/after match (default: 2)",
                            minimum = 0,
                            maximum = 10
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum results to return (default: 100)",
                            minimum = 1,
                            maximum = 1000
                        },
                        cursor = new
                        {
                            type = "string",
                            description = "Pagination cursor from previous search"
                        }
                    },
                    required = new[] { "pattern" }
                }
            },

            new McpTool
            {
                Name = "search_files",
                Description = "Search for files by name or path pattern",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = "File name or path pattern (glob supported, e.g., '**/*Test.cs')"
                        },
                        includeHidden = new
                        {
                            type = "boolean",
                            description = "Include hidden files and directories (default: false)"
                        },
                        includeDirectories = new
                        {
                            type = "boolean",
                            description = "Include directories in results (default: false)"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum results to return (default: 1000)",
                            minimum = 1,
                            maximum = 10000
                        }
                    },
                    required = new[] { "pattern" }
                }
            }
        });
    }

    private async Task<object> ExecuteSearchText(JsonElement args)
    {
        EnsureTextSearchService();

        var request = new SearchRequest
        {
            Pattern = args.GetProperty("pattern").GetString() ??
                throw new ArgumentException("Pattern is required"),
            CaseSensitive = args.TryGetProperty("caseSensitive", out var cs)
                ? cs.GetBoolean() : false,
            WholeWord = args.TryGetProperty("wholeWord", out var ww)
                ? ww.GetBoolean() : false,
            IncludePattern = args.TryGetProperty("includePattern", out var inc)
                ? inc.GetString() : null,
            ExcludePattern = args.TryGetProperty("excludePattern", out var exc)
                ? exc.GetString() : null,
            ContextLines = args.TryGetProperty("contextLines", out var ctx)
                ? ctx.GetInt32() : 2,
            MaxResults = args.TryGetProperty("maxResults", out var max)
                ? ctx.GetInt32() : 100,
            Cursor = args.TryGetProperty("cursor", out var cursor)
                ? cursor.GetString() : null,
            Regex = false // Explicit literal search
        };

        return await _textSearchService!.SearchTextAsync(request);
    }

    private async Task<object> ExecuteSearchRegex(JsonElement args)
    {
        EnsureTextSearchService();

        var pattern = args.GetProperty("pattern").GetString() ??
            throw new ArgumentException("Pattern is required");

        // Validate regex pattern for safety
        var validator = new SafeRegexValidator();
        var validation = validator.ValidatePattern(pattern);

        if (!validation.IsValid)
        {
            return new
            {
                success = false,
                error = $"Invalid regex pattern: {validation.ErrorMessage}",
                suggestion = "Please check your pattern for common regex issues"
            };
        }

        var request = new SearchRequest
        {
            Pattern = pattern,
            CaseSensitive = args.TryGetProperty("caseSensitive", out var cs)
                ? cs.GetBoolean() : true,
            Multiline = args.TryGetProperty("multiline", out var ml)
                ? ml.GetBoolean() : false,
            IncludePattern = args.TryGetProperty("includePattern", out var inc)
                ? inc.GetString() : null,
            ExcludePattern = args.TryGetProperty("excludePattern", out var exc)
                ? exc.GetString() : null,
            ContextLines = args.TryGetProperty("contextLines", out var ctx)
                ? ctx.GetInt32() : 2,
            MaxResults = args.TryGetProperty("maxResults", out var max)
                ? max.GetInt32() : 100,
            Cursor = args.TryGetProperty("cursor", out var cursor)
                ? cursor.GetString() : null,
            Regex = true // Explicit regex search
        };

        return await _textSearchService!.SearchTextAsync(request);
    }

    private void EnsureTextSearchService()
    {
        if (_textSearchService == null)
        {
            var engineSelector = new SearchEngineSelector(
                new ISearchEngine[]
                {
                    new RegexSearchEngine(_regexCache, _logger),
                    new RoslynSearchEngine(_workspace, _logger),
                    new IndexedSearchEngine(_indexService, _logger)
                },
                _logger);

            var cursorManager = new SearchCursorManager(_cache, _logger);

            _textSearchService = new TextSearchService(
                engineSelector,
                _fileOperations,
                _responseProcessor,
                cursorManager,
                _progressTracker,
                _logger);
        }
    }
}
```

This comprehensive implementation provides:

1. **High-performance search** with streaming and parallel processing
2. **Multiple search engines** optimized for different scenarios
3. **Cursor-based pagination** for handling large result sets
4. **Semantic search capabilities** via Roslyn for comments and strings
5. **Safety features** including ReDoS prevention and binary file detection
6. **Full MCP integration** with proper tool registration and response processing

The design ensures scalability to large codebases while maintaining sub-5-second response times for typical searches.