using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// File list, read, write, edit and text search tools
/// </summary>
public partial class McpToolRegistry
{
    private ToolCallResult ExecuteFileList(JsonDocument arguments)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        string? pattern = null;
        bool includeHidden = false;

        if (arguments.RootElement.TryGetProperty("pattern", out var patternElement))
        {
            pattern = patternElement.GetString();
        }

        if (arguments.RootElement.TryGetProperty("include_hidden", out var hiddenElement))
        {
            includeHidden = hiddenElement.GetBoolean();
        }

        var result = _fileOperations.ListFiles(pattern, includeHidden);
        return new ToolCallResult
        {
            Success = true,
            Result = new
            {
                Files = result.Files.Select(f => new
                {
                    f.Path,
                    f.RelativePath,
                    f.Size,
                    f.LastModified,
                    f.IsHidden
                }),
                result.TotalFiles,
                result.Pattern
            }
        };
    }

    private async Task<ToolCallResult> ExecuteFileRead(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        var result = await _fileOperations.ReadFileAsync(path, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.Content,
                result.Encoding,
                result.LineCount,
                result.Size
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFileWrite(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        var content = arguments.RootElement.GetProperty("content").GetString();

        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        if (content == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Content is required"
            };
        }

        bool createDirectories = true;
        if (arguments.RootElement.TryGetProperty("create_directories", out var createDirElement))
        {
            createDirectories = createDirElement.GetBoolean();
        }

        var result = await _fileOperations.WriteFileAsync(path, content, createDirectories, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.BytesWritten,
                result.Created
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteFileEdit(JsonDocument arguments, CancellationToken ct)
    {
        if (_fileOperations == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is open"
            };
        }

        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        var edits = ParseTextEdits(arguments.RootElement.GetProperty("edits"));

        var result = await _fileOperations.EditFileAsync(path, edits, ct);
        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Path,
                result.EditsApplied,
                result.NewContent
            } : null,
            Error = result.Error
        };
    }

    private async Task<ToolCallResult> ExecuteSearchText(JsonDocument arguments, CancellationToken ct)
    {
        if (_searchService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Search service not available"
            };
        }

        var pattern = arguments.RootElement.GetProperty("pattern").GetString();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Pattern is required"
            };
        }

        var request = new Models.Search.SearchRequest
        {
            Pattern = pattern,
            TargetPath = arguments.RootElement.TryGetProperty("target_path", out var targetPathProp) && targetPathProp.ValueKind != JsonValueKind.Null
                ? targetPathProp.GetString()
                : null,
            Regex = arguments.RootElement.TryGetProperty("regex", out var regexProp) && regexProp.ValueKind == JsonValueKind.True,
            CaseSensitive = !arguments.RootElement.TryGetProperty("case_sensitive", out var caseProp) || caseProp.ValueKind != JsonValueKind.False,
            IncludePattern = arguments.RootElement.TryGetProperty("include_pattern", out var incProp) && incProp.ValueKind != JsonValueKind.Null
                ? incProp.GetString()
                : null,
            ExcludePattern = arguments.RootElement.TryGetProperty("exclude_pattern", out var excProp) && excProp.ValueKind != JsonValueKind.Null
                ? excProp.GetString()
                : null,
            ContextLines = arguments.RootElement.TryGetProperty("context_lines", out var contextProp) && contextProp.ValueKind == JsonValueKind.Number
                ? contextProp.GetInt32()
                : 2,
            MaxResults = arguments.RootElement.TryGetProperty("max_results", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number
                ? maxProp.GetInt32()
                : 100,
            Offset = arguments.RootElement.TryGetProperty("offset", out var offsetProp) && offsetProp.ValueKind == JsonValueKind.Number
                ? offsetProp.GetInt32()
                : 0
        };

        var result = await _searchService.SearchTextAsync(request, ct);

        return new ToolCallResult
        {
            Success = result.Success,
            Result = result.Success ? new
            {
                result.Success,
                result.TotalMatches,
                result.Returned,
                result.Offset,
                result.HasMore,
                Matches = result.Matches.Select(m => new
                {
                    m.FilePath,
                    m.LineNumber,
                    m.ColumnNumber,
                    m.MatchText,
                    m.ContextBefore,
                    m.ContextAfter,
                    m.LineContent
                }),
                result.FilesSearched,
                result.SearchDurationMs
            } : null,
            Error = result.ErrorMessage
        };
    }
}
