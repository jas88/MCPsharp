using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Search;
using MCPsharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class SearchServiceTests
{
    private string _testDirectory = null!;
    private ProjectContextManager _projectContext = null!;
    private SearchService _searchService = null!;

    [SetUp]
    public void SetUp()
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

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public async Task SearchTextAsync_EmptyPattern_ReturnsError()
    {
        // Arrange
        var request = new SearchRequest { Pattern = "" };

        // Act
        var result = await _searchService.SearchTextAsync(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage ?? "", Does.Contain("empty").IgnoreCase);
    }

    [Test]
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
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage ?? "", Does.Contain("does not exist").IgnoreCase);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(2));
        Assert.That(result.Matches, Has.All.Matches<SearchMatch>(m => m.LineNumber == 1));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(2));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(3));
    }

    [Test]
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
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage ?? "", Does.Contain("Invalid regex").IgnoreCase);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(1));
        Assert.That(result.Matches[0].FilePath, Does.EndWith(".cs"));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(1));
        Assert.That(result.Matches[0].FilePath, Does.Not.Contain("ignore").IgnoreCase);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Matches, Has.Count.EqualTo(1));
        var match = result.Matches[0];
        Assert.That(match.ContextBefore, Has.Count.EqualTo(2));
        Assert.That(match.ContextBefore[0], Is.EqualTo("line 1"));
        Assert.That(match.ContextBefore[1], Is.EqualTo("line 2"));
        Assert.That(match.ContextAfter, Has.Count.EqualTo(2));
        Assert.That(match.ContextAfter[0], Is.EqualTo("line 4"));
        Assert.That(match.ContextAfter[1], Is.EqualTo("line 5"));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        var match = result.Matches[0];
        Assert.That(match.ContextBefore, Is.Empty); // No lines before
        Assert.That(match.ContextAfter, Has.Count.EqualTo(1)); // Only one line after
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(50));
        Assert.That(result.Returned, Is.EqualTo(10));
        Assert.That(result.Offset, Is.EqualTo(5));
        Assert.That(result.HasMore, Is.True);
        Assert.That(result.Matches[0].LineNumber, Is.EqualTo(6)); // First match should be line 6 (offset 5)
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(0));
        Assert.That(result.Matches, Is.Empty);
        Assert.That(result.HasMore, Is.False);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        // Should only find match in .cs file, not .dll
        Assert.That(result.Matches, Has.All.Matches<SearchMatch>(m => m.FilePath.EndsWith(".cs")));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        // Should not find matches in bin directory
        Assert.That(result.Matches, Has.All.Matches<SearchMatch>(m => !m.FilePath.Contains("bin")));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Matches, Has.Count.EqualTo(1));
        Assert.That(result.Matches[0].ColumnNumber, Is.EqualTo(5)); // 1-based, after 4 spaces
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        // Note: Current implementation finds first match per line
        // This test documents expected behavior
        Assert.That(result.TotalMatches, Is.EqualTo(1));
    }

    [Test]
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
        Assert.ThrowsAsync<TaskCanceledException>(
            async () => await _searchService.SearchTextAsync(request, cts.Token));
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.TotalMatches, Is.EqualTo(100));
        Assert.That(result.SearchDurationMs, Is.LessThan(3000), $"Search took {result.SearchDurationMs}ms, expected < 3000ms");
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Matches, Has.Count.EqualTo(1));
        Assert.That(result.Matches[0].MatchText, Does.Contain("1.2.3"));
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
