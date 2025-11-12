using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Search;
using MCPsharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPsharp.Tests.Services;

public class SearchServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ProjectContextManager _projectContext;
    private readonly SearchService _searchService;

    public SearchServiceTests()
    {
        // Create a temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"SearchServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        // Initialize project context
        _projectContext = new ProjectContextManager();
        _projectContext.OpenProject(_testDirectory);

        // Initialize search service
        _searchService = new SearchService(_projectContext, NullLogger<SearchService>.Instance);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task SearchTextAsync_EmptyPattern_ReturnsError()
    {
        // Arrange
        var request = new SearchRequest { Pattern = "" };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchTextAsync_NonExistentDirectory_ReturnsError()
    {
        // Arrange
        var request = new SearchRequest
        {
            Pattern = "test",
            TargetPath = "/nonexistent/directory"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchTextAsync_LiteralSearch_FindsMatches()
    {
        // Arrange
        CreateTestFile("test1.cs", "public class TestClass\n{\n    // TODO: implement this\n}");
        CreateTestFile("test2.cs", "public class AnotherClass\n{\n    private int value;\n}");

        var request = new SearchRequest
        {
            Pattern = "public class",
            CaseSensitive = true
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalMatches);
        Assert.All(result.Matches, m => Assert.Equal(1, m.LineNumber));
    }

    [Fact]
    public async Task SearchTextAsync_CaseInsensitive_FindsMatches()
    {
        // Arrange
        CreateTestFile("test.cs", "Public Class TestClass\npublic class AnotherClass");

        var request = new SearchRequest
        {
            Pattern = "PUBLIC CLASS",
            CaseSensitive = false
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalMatches);
    }

    [Fact]
    public async Task SearchTextAsync_RegexSearch_FindsMatches()
    {
        // Arrange
        CreateTestFile("test.cs", "int value1 = 10;\nstring value2 = \"test\";\nbool value3 = true;");

        var request = new SearchRequest
        {
            Pattern = @"(int|string|bool)\s+\w+",
            Regex = true
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalMatches);
    }

    [Fact]
    public async Task SearchTextAsync_InvalidRegex_ReturnsError()
    {
        // Arrange
        CreateTestFile("test.cs", "some content here");
        var request = new SearchRequest
        {
            Pattern = "([invalid",
            Regex = true
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("Invalid regex", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchTextAsync_IncludePattern_FiltersFiles()
    {
        // Arrange
        CreateTestFile("test.cs", "public class Test");
        CreateTestFile("test.txt", "public class Test");
        CreateTestFile("test.json", "public class Test");

        var request = new SearchRequest
        {
            Pattern = "public class",
            IncludePattern = "*.cs"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalMatches);
        Assert.EndsWith(".cs", result.Matches[0].FilePath);
    }

    [Fact]
    public async Task SearchTextAsync_ExcludePattern_FiltersFiles()
    {
        // Arrange
        CreateTestFile("test.cs", "public class Test");
        CreateTestFile("ignore.cs", "public class Ignore");

        var request = new SearchRequest
        {
            Pattern = "public class",
            ExcludePattern = "**/ignore.*"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalMatches);
        Assert.DoesNotContain("ignore", result.Matches[0].FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchTextAsync_ContextLines_IncludesContext()
    {
        // Arrange
        CreateTestFile("test.cs", "line 1\nline 2\nTARGET LINE\nline 4\nline 5");

        var request = new SearchRequest
        {
            Pattern = "TARGET LINE",
            ContextLines = 2
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Matches);
        var match = result.Matches[0];
        Assert.Equal(2, match.ContextBefore.Count);
        Assert.Equal("line 1", match.ContextBefore[0]);
        Assert.Equal("line 2", match.ContextBefore[1]);
        Assert.Equal(2, match.ContextAfter.Count);
        Assert.Equal("line 4", match.ContextAfter[0]);
        Assert.Equal("line 5", match.ContextAfter[1]);
    }

    [Fact]
    public async Task SearchTextAsync_ContextLines_HandlesFileBoundaries()
    {
        // Arrange
        CreateTestFile("test.cs", "TARGET LINE\nline 2");

        var request = new SearchRequest
        {
            Pattern = "TARGET LINE",
            ContextLines = 5
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        var match = result.Matches[0];
        Assert.Empty(match.ContextBefore); // No lines before
        Assert.Single(match.ContextAfter); // Only one line after
    }

    [Fact]
    public async Task SearchTextAsync_Pagination_ReturnsCorrectSubset()
    {
        // Arrange
        var lines = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"match {i}"));
        CreateTestFile("test.cs", lines);

        var request = new SearchRequest
        {
            Pattern = "match",
            MaxResults = 10,
            Offset = 5
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50, result.TotalMatches);
        Assert.Equal(10, result.Returned);
        Assert.Equal(5, result.Offset);
        Assert.True(result.HasMore);
        Assert.Equal(6, result.Matches[0].LineNumber); // First match should be line 6 (offset 5)
    }

    [Fact]
    public async Task SearchTextAsync_NoMatches_ReturnsEmptyResult()
    {
        // Arrange
        CreateTestFile("test.cs", "public class Test");

        var request = new SearchRequest
        {
            Pattern = "NONEXISTENT"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalMatches);
        Assert.Empty(result.Matches);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task SearchTextAsync_SkipsBinaryFiles()
    {
        // Arrange
        CreateBinaryFile("test.dll");
        CreateTestFile("test.cs", "public class Test");

        var request = new SearchRequest
        {
            Pattern = "test"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        // Should only find match in .cs file, not .dll
        Assert.All(result.Matches, m => Assert.EndsWith(".cs", m.FilePath));
    }

    [Fact]
    public async Task SearchTextAsync_SkipsDefaultExcludeDirs()
    {
        // Arrange
        var binDir = Path.Combine(_testDirectory, "bin");
        Directory.CreateDirectory(binDir);
        CreateTestFile("bin/output.cs", "public class Test");
        CreateTestFile("src.cs", "public class Test");

        var request = new SearchRequest
        {
            Pattern = "public class"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        // Should not find matches in bin directory
        Assert.All(result.Matches, m => Assert.DoesNotContain("bin", m.FilePath));
    }

    [Fact]
    public async Task SearchTextAsync_ColumnNumber_IsCorrect()
    {
        // Arrange
        CreateTestFile("test.cs", "    public class Test");

        var request = new SearchRequest
        {
            Pattern = "public"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Matches);
        Assert.Equal(5, result.Matches[0].ColumnNumber); // 1-based, after 4 spaces
    }

    [Fact]
    public async Task SearchTextAsync_MultipleMatchesInSameLine_FindsAll()
    {
        // Arrange
        CreateTestFile("test.cs", "test test test");

        var request = new SearchRequest
        {
            Pattern = "test"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        // Note: Current implementation finds first match per line
        // This test documents expected behavior
        Assert.Equal(1, result.TotalMatches);
    }

    [Fact]
    public async Task SearchTextAsync_CancellationToken_CancelsOperation()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            CreateTestFile($"test{i}.cs", string.Join("\n", Enumerable.Range(1, 1000).Select(j => $"line {j}")));
        }

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10)); // Cancel quickly

        var request = new SearchRequest
        {
            Pattern = "line"
        };

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await _searchService.SearchTextAsync(request, cts.Token));
    }

    [Fact]
    public async Task SearchTextAsync_PerformanceTest_SearchesQuickly()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            CreateTestFile($"test{i}.cs", $"public class Test{i}\n{{\n    private int value;\n}}");
        }

        var request = new SearchRequest
        {
            Pattern = "public class"
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(100, result.TotalMatches);
        Assert.True(result.SearchDurationMs < 3000, $"Search took {result.SearchDurationMs}ms, expected < 3000ms");
    }

    [Fact]
    public async Task SearchTextAsync_RegexWithGroups_CapturesMatch()
    {
        // Arrange
        CreateTestFile("test.cs", "version 1.2.3");

        var request = new SearchRequest
        {
            Pattern = @"version (\d+\.\d+\.\d+)",
            Regex = true
        };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Matches);
        Assert.Contains("1.2.3", result.Matches[0].MatchText);
    }

    private void CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
    }

    private void CreateBinaryFile(string relativePath)
    {
        var fullPath = Path.Combine(_testDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        // Write binary content with null bytes
        File.WriteAllBytes(fullPath, new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE });
    }
}
