using System;
using System.IO;
using System.Threading.Tasks;
using MCPsharp.Models;
using MCPsharp.Services.Phase2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DebugTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Create a test setup similar to the failing test
            var tempDir = Path.Combine(Path.GetTempPath(), "DebugTest");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create appsettings.json like the test
                await File.WriteAllTextAsync(Path.Combine(tempDir, "appsettings.json"), @"{
  ""Payment"": {
    ""DefaultCurrency"": ""USD"",
    ""AllowedCurrencies"": [""USD"", ""EUR"", ""GBP""]
  }
}");

                // Create C# file like the test
                var srcDir = Path.Combine(tempDir, "src");
                Directory.CreateDirectory(srcDir);

                await File.WriteAllTextAsync(Path.Combine(srcDir, "Payment.cs"), @"
public class Payment
{
    public decimal Amount { get; set; }
    public string Currency { get; set; }
}");

                // Test the GetProjectRoot logic
                var filePath = Path.Combine(srcDir, "Payment.cs");
                var projectRoot = GetProjectRoot(filePath);

                Console.WriteLine($"File path: {filePath}");
                Console.WriteLine($"Project root: {projectRoot}");
                Console.WriteLine($"Appsettings exists: {File.Exists(Path.Combine(projectRoot, "appsettings.json"))}");

                // Test config file search
                var configFiles = Directory.GetFiles(projectRoot, "*.json", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(projectRoot, "*.yml", SearchOption.AllDirectories))
                    .Concat(Directory.GetFiles(projectRoot, "*.yaml", SearchOption.AllDirectories))
                    .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"));

                Console.WriteLine($"Config files found: {string.Join(", ", configFiles.Select(Path.GetFileName))}");

                // Test content search
                var symbolName = "Currency";
                var appsettingsPath = Path.Combine(projectRoot, "appsettings.json");
                if (File.Exists(appsettingsPath))
                {
                    var content = await File.ReadAllTextAsync(appsettingsPath);
                    Console.WriteLine($"Appsettings content contains '{symbolName}': {content.Contains(symbolName, StringComparison.OrdinalIgnoreCase)}");
                    Console.WriteLine($"Content: {content}");
                }
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        private static string GetProjectRoot(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            while (dir != null && !string.IsNullOrEmpty(dir))
            {
                // Look for common project root indicators
                if (Directory.GetFiles(dir, "*.sln").Any() ||
                    Directory.GetFiles(dir, "*.csproj").Any() ||
                    Directory.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }

            // If no project root indicators found, use the immediate directory
            // This handles test scenarios where projects are created in temp directories
            var immediateDir = Path.GetDirectoryName(filePath);
            return immediateDir ?? filePath;
        }
    }
}