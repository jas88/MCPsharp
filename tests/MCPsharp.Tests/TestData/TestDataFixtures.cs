using System;
using System.Collections.Generic;
using System.IO;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Tests.TestData;

/// <summary>
/// Test data fixtures for various test scenarios
/// </summary>
public static class TestDataFixtures
{
    /// <summary>
    /// Sample project context for testing
    /// </summary>
    public static ProjectContext SampleProjectContext => new()
    {
        RootPath = "/test/project"
    };

    /// <summary>
    /// Sample method signature for testing
    /// </summary>
    public static MethodSignature SampleMethodSignature => new()
    {
        Name = "ProcessData",
        ReturnType = "string",
        Parameters = new List<ParameterInfo>
        {
            new() { Name = "input", Type = "string", Position = 0 }
        },
        ContainingType = "TestProject.Services.SampleService",
        Accessibility = "public",
        IsStatic = false,
        IsVirtual = false,
        IsAbstract = false,
        IsOverride = false,
        IsExtension = false,
        IsAsync = false,
        FullyQualifiedName = "TestProject.Services.SampleService.ProcessData(string)"
    };

    /// <summary>
    /// Sample text edit for testing
    /// </summary>
    public static TextEdit SampleTextEdit => new ReplaceEdit
    {
        StartLine = 10,
        StartColumn = 15,
        EndLine = 10,
        EndColumn = 25,
        NewText = "replacementText"
    };

    /// <summary>
    /// Sample bulk edit request for testing
    /// </summary>
    public static BulkEditRequest SampleBulkEditRequest => new()
    {
        OperationType = BulkEditOperationType.BulkReplace,
        Files = new[] { "*.cs" },
        RegexPattern = @"oldText",
        RegexReplacement = "newText",
        Options = new BulkEditOptions
        {
            MaxParallelism = 4,
            CreateBackups = true,
            PreviewMode = false,
            ValidateChanges = true,
            MaxFileSize = 1024 * 1024, // 1MB
            StopOnFirstError = false
        }
    };

    /// <summary>
    /// Sample bulk edit condition for testing
    /// </summary>
    public static BulkEditCondition SampleCondition => new()
    {
        ConditionType = BulkConditionType.FileContains,
        Pattern = "TODO:",
        Negate = false,
        Parameters = new Dictionary<string, object>()
    };

    /// <summary>
    /// Sample refactor pattern for testing
    /// </summary>
    public static BulkRefactorPattern SampleRefactorPattern => new()
    {
        RefactorType = BulkRefactorType.RenameSymbol,
        TargetPattern = "OldMethodName",
        ReplacementPattern = "NewMethodName",
        Parameters = new Dictionary<string, object>
        {
            ["oldName"] = "OldMethodName",
            ["newName"] = "NewMethodName"
        }
    };

    /// <summary>
    /// Sample streaming file configuration
    /// </summary>
    public static StreamProcessRequest SampleStreamingConfig => new()
    {
        ChunkSize = 64 * 1024, // 64KB
        ProcessorType = StreamProcessorType.LineProcessor,
        CreateCheckpoint = true,
        EnableCompression = false
    };

    /// <summary>
    /// Sample validation request for testing
    /// TODO: Implement ValidationRequest class or replace with appropriate validation model
    /// </summary>
    // public static ValidationRequest SampleValidationRequest => new()
    // {
    //     OperationType = ValidationOperationType.Compilation,
    //     TargetPaths = new[] { "/test/project/src" },
    //     Options = new ValidationOptions
    //     {
    //         EnableSemanticAnalysis = true,
    //         EnableSyntacticAnalysis = true,
    //         EnablePerformanceAnalysis = false,
    //         MaxDiagnostics = 100
    //     }
    // };

    /// <summary>
    /// File paths for different test scenarios
    /// </summary>
    public static class TestFilePaths
    {
        public const string SimpleServiceFile = "Services/SampleService.cs";
        public const string InheritanceFile = "Models/InheritanceExample.cs";
        public const string CallChainFile = "Workflows/DataWorkflow.cs";
        public const string LargeFile = "Large/LargeService.cs";
        public const string GenericFile = "Generics/Repository.cs";
        public const string ErrorFile = "Errors/ErrorProneService.cs";
        public const string ConfigFile = "appsettings.json";
        public const string ReadmeFile = "README.md";
    }

    /// <summary>
    /// Test file contents for different scenarios
    /// </summary>
    public static class TestFileContents
    {
        public const string JsonConfig = @"
{
    ""Logging"": {
        ""LogLevel"": {
            ""Default"": ""Information"",
            ""Microsoft.AspNetCore"": ""Warning""
        }
    },
    ""AllowedHosts"": ""*"",
    ""ConnectionStrings"": {
        ""DefaultConnection"": ""Server=(localdb)\\mssqllocaldb;Database=TestDb""
    }
}";

        public const string Markdown = @"
# Test Project

This is a sample markdown file for testing.

## Features

- Feature 1
- Feature 2
- Feature 3

## Usage

```csharp
var service = new SampleService();
var result = service.ProcessData(""test"");
```

## API Reference

| Method | Description |
|--------|-------------|
| ProcessData | Processes input data |
| GetDataAsync | Gets data asynchronously |";

        public const string XmlConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <appSettings>
    <add key=""TestSetting"" value=""TestValue"" />
  </appSettings>
  <connectionStrings>
    <add name=""DefaultConnection"" connectionString=""Server=(localdb)\\mssqllocaldb;Database=TestDb"" />
  </connectionStrings>
</configuration>";
    }

    /// <summary>
    /// Performance test data
    /// </summary>
    public static class PerformanceTestData
    {
        /// <summary>
        /// Large text content for performance testing
        /// </summary>
        public static string GenerateLargeText(int lines = 10000)
        {
            var random = new Random(42); // Fixed seed for reproducible tests
            var linesList = new List<string>(lines);

            for (int i = 0; i < lines; i++)
            {
                var line = $"Line {i:D5}: {GenerateRandomWords(random, random.Next(5, 15))}";
                linesList.Add(line);
            }

            return string.Join("\n", linesList);
        }

        public static string GenerateRandomWords(Random random, int count)
        {
            var words = new List<string>();
            var wordLengths = new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

            for (int i = 0; i < count; i++)
            {
                var length = wordLengths[random.Next(wordLengths.Length)];
                var word = GenerateRandomWord(random, length);
                words.Add(word);
            }

            return string.Join(" ", words);
        }

        private static string GenerateRandomWord(Random random, int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            var result = new char[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = chars[random.Next(chars.Length)];
            }

            return new string(result);
        }

        /// <summary>
        /// Creates a large JSON structure for testing
        /// </summary>
        public static string GenerateLargeJson(int objectCount = 1000)
        {
            var json = new System.Text.StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"metadata\": {");
            json.AppendLine("    \"version\": \"1.0\",");
            json.AppendLine($"    \"objectCount\": {objectCount},");
            json.AppendLine($"    \"generatedAt\": \"{DateTime.UtcNow:O}\"");
            json.AppendLine("  },");
            json.AppendLine("  \"items\": [");

            for (int i = 0; i < objectCount; i++)
            {
                json.AppendLine("    {");
                json.AppendLine($"      \"id\": {i},");
                json.AppendLine($"      \"name\": \"Item {i}\",");
                json.AppendLine($"      \"value\": {i * 1.5:F2},");
                json.AppendLine($"      \"active\": {(i % 2 == 0)},");
                json.AppendLine($"      \"description\": \"Description for item {i}\",");
                json.AppendLine($"      \"tags\": [\"tag{i % 10}\", \"category{i % 5}\"]");
                json.Append("    }");

                if (i < objectCount - 1)
                    json.AppendLine(",");
                else
                    json.AppendLine();
            }

            json.AppendLine("  ]");
            json.AppendLine("}");

            return json.ToString();
        }
    }

    /// <summary>
    /// Test scenarios for edge cases
    /// </summary>
    public static class EdgeCaseData
    {
        /// <summary>
        /// Empty file content
        /// </summary>
        public const string EmptyFile = "";

        /// <summary>
        /// File with only whitespace
        /// </summary>
        public const string WhitespaceOnly = "   \n\t  \n   ";

        /// <summary>
        /// File with very long lines
        /// </summary>
        public static string LongLineFile => new string('x', 10000) + "\n" + new string('y', 15000);

        /// <summary>
        /// File with special characters
        /// </summary>
        public const string SpecialCharacters = @"
Special characters: áéíóú ñ ç ø å 中文 العربية русский
Unicode emojis: 🚀 🎉 💻 🧪 ✅ ❌
Control characters: \t \r \n
Mathematical symbols: ∑ ∏ ∫ ∂ ∇ ∆
Quotes: ""'' ""'' ''"" ""'' '';

XML entities: &lt; &gt; &amp; &quot; &apos;
HTML entities: &nbsp; &mdash; &ndash; &lsquo; &rsquo;
";

        /// <summary>
        /// File with encoding challenges
        /// </summary>
        public static string EncodingFile = "UTF-8 test: café résumé naïve\n" +
                                         "UTF-16 test: 中文 العربية русский\n" +
                                         "Mixed encoding: Test 🚀 with emojis and accénted charácters";

        /// <summary>
        /// Extremely large file content (simulated)
        /// </summary>
        public static string GenerateExtremelyLargeFile()
        {
            var content = new System.Text.StringBuilder();
            var random = new Random(42);

            for (int i = 0; i < 100000; i++) // 100k lines
            {
                content.AppendLine($"Line {i:D6}: {PerformanceTestData.GenerateRandomWords(random, random.Next(3, 20))}");
            }

            return content.ToString();
        }
    }
}