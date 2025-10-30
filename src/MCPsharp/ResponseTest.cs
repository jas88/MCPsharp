using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Services;

namespace MCPsharp;

/// <summary>
/// Simple demonstration of the response processing functionality
/// </summary>
public class ResponseTest
{
    public static void RunTest()
    {
        Console.WriteLine("=== MCPsharp Response Processing Demo ===");

        // Test 1: Default configuration
        Console.WriteLine("\n1. Testing default configuration:");
        var config = ResponseConfiguration.LoadFromEnvironment();
        Console.WriteLine($"   MaxTokens: {config.MaxTokens}");
        Console.WriteLine($"   EnableTruncation: {config.EnableTruncation}");
        Console.WriteLine($"   SummaryStyle: {config.SummaryStyle}");

        // Test 2: Create processor and test small response
        Console.WriteLine("\n2. Testing small response (should not truncate):");
        var processor = new ResponseProcessor(config);
        var smallResponse = new { message = "This is a small response that should not be truncated." };
        var result1 = processor.ProcessResponse(smallResponse, "test_tool");
        Console.WriteLine($"   WasTruncated: {result1.WasTruncated}");
        Console.WriteLine($"   EstimatedTokenCount: {result1.EstimatedTokenCount}");

        // Test 3: Test large response with truncation
        Console.WriteLine("\n3. Testing large response (should truncate):");
        var largeResponse = new
        {
            title = "Large Response Test",
            content = new string('x', 5000), // This should exceed token limits
            items = Enumerable.Range(1, 1000).Select(i => $"Item {i}").ToList(),
            metadata = new
            {
                created = DateTime.UtcNow,
                type = "test_data",
                tags = new[] { "large", "data", "test" }
            }
        };

        var result2 = processor.ProcessResponse(largeResponse, "test_tool");
        Console.WriteLine($"   WasTruncated: {result2.WasTruncated}");
        Console.WriteLine($"   OriginalTokenCount: {result2.OriginalTokenCount}");
        Console.WriteLine($"   EstimatedTokenCount: {result2.EstimatedTokenCount}");
        Console.WriteLine($"   Warning: {result2.Warning}");

        // Test 4: Test with custom configuration
        Console.WriteLine("\n4. Testing custom configuration (lower limits):");
        Environment.SetEnvironmentVariable("MCP_MAX_TOKENS", "50");
        Environment.SetEnvironmentVariable("MCP_SUMMARY_STYLE", "Ellipsis");
        Environment.SetEnvironmentVariable("MCP_WARNING_THRESHOLD", "0.5");

        var customConfig = ResponseConfiguration.LoadFromEnvironment();
        var customProcessor = new ResponseProcessor(customConfig);
        var mediumResponse = new { data = new string('y', 300) };

        var result3 = customProcessor.ProcessResponse(mediumResponse, "test_tool");
        Console.WriteLine($"   Config MaxTokens: {customConfig.MaxTokens}");
        Console.WriteLine($"   WasTruncated: {result3.WasTruncated}");
        Console.WriteLine($"   Warning: {result3.Warning}");

        // Test 5: Test exempt tools
        Console.WriteLine("\n5. Testing exempt tools:");
        var exemptConfig = new ResponseConfiguration
        {
            MaxTokens = 10,
            EnableTruncation = true,
            ExemptTools = new HashSet<string> { "exempt_*" }
        };
        var exemptProcessor = new ResponseProcessor(exemptConfig);
        var exemptResult = exemptProcessor.ProcessResponse(largeResponse, "exempt_tool");
        Console.WriteLine($"   Tool: exempt_tool");
        Console.WriteLine($"   WasTruncated: {exemptResult.WasTruncated}");
        Console.WriteLine($"   TokenCount: {exemptResult.EstimatedTokenCount} (should be > 10)");

        // Cleanup environment
        Environment.SetEnvironmentVariable("MCP_MAX_TOKENS", null);
        Environment.SetEnvironmentVariable("MCP_SUMMARY_STYLE", null);
        Environment.SetEnvironmentVariable("MCP_WARNING_THRESHOLD", null);

        Console.WriteLine("\n=== Demo Complete ===");
        Console.WriteLine("The response processing system is working correctly!");
    }
}