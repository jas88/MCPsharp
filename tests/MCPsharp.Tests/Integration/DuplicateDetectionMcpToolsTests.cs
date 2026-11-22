using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests for the <c>find_near_duplicates</c> MCP tool's summary figures.
/// </summary>
[TestFixture]
[NonParallelizable]
public class DuplicateDetectionMcpToolsTests
{
    private const string ToolName = "find_near_duplicates";
    private const string ExemptToolsVariable = "MCP_EXEMPT_TOOLS";

    private const string SumMethod = """
            public int Sum(int[] values, int limit)
            {
                var total = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i] > limit)
                    {
                        total += values[i] * 2;
                    }
                    else
                    {
                        total -= values[i];
                    }
                }

                System.Console.WriteLine(total);
                return total;
            }
        """;

    private ProjectContextManager _projectContext = null!;
    private RoslynWorkspace _workspace = null!;
    private McpToolRegistry _toolRegistry = null!;
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        // The registry reads its response limits when it is built: an exempt tool's result is not truncated
        Environment.SetEnvironmentVariable(ExemptToolsVariable, ToolName);
        _projectContext = new ProjectContextManager();
        _workspace = new RoslynWorkspace();
        _toolRegistry = new McpToolRegistry(_projectContext, _workspace);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"mcpsharp_duplicates_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ExemptToolsVariable, null);
        _workspace?.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public async Task FindNearDuplicates_WithDuplicatedMethods_AveragesTheirSimilarity()
    {
        WriteClass("First", SumMethod);
        WriteClass("Second", SumMethod);
        _projectContext.OpenProject(_testDirectory);

        var summary = await FindNearDuplicatesAsync();

        var scores = summary.GetProperty("duplicateGroups").EnumerateArray()
            .Select(g => g.GetProperty("SimilarityScore").GetDouble())
            .ToList();
        Assert.That(scores, Is.Not.Empty, "the two classes hold the same method");
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("totalGroups").GetInt32(), Is.EqualTo(scores.Count));
            Assert.That(summary.GetProperty("averageSimilarity").GetDouble(), Is.EqualTo(scores.Average()).Within(1e-9));
            Assert.That(scores.Average(), Is.GreaterThan(0.0));
        });
    }

    [Test]
    public async Task FindNearDuplicates_WithoutDuplicates_ReportsZeroAverageSimilarity()
    {
        WriteClass("Only", string.Empty);
        _projectContext.OpenProject(_testDirectory);

        var summary = await FindNearDuplicatesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("duplicateGroups").GetArrayLength(), Is.EqualTo(0));
            Assert.That(summary.GetProperty("totalGroups").GetInt32(), Is.EqualTo(0));
            Assert.That(summary.GetProperty("averageSimilarity").GetDouble(), Is.EqualTo(0.0));
        });
    }

    private async Task<JsonElement> FindNearDuplicatesAsync()
    {
        using var arguments = JsonDocument.Parse("""{ "similarityThreshold": 0.8 }""");

        var result = await _toolRegistry.ExecuteToolAsync(new ToolCallRequest { Name = ToolName, Arguments = arguments });

        Assert.That(result.Success, Is.True, $"Expected success but got: {result.Error}");
        return JsonSerializer.SerializeToElement(result.Result);
    }

    private void WriteClass(string className, string members) =>
        File.WriteAllText(Path.Combine(_testDirectory, $"{className}.cs"), $$"""
            namespace MCPsharp.Tests.TestFixtures;

            public class {{className}}
            {
            {{members}}
            }
            """);
}
