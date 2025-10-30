using System;
using System.Collections.Generic;
using MCPsharp.Models.Analyzers;

// Test compilation of our new model classes
public class TestClasses
{
    public static void Main()
    {
        // Test AnalyzerOptions
        var options = new AnalyzerOptions
        {
            EnableSemanticAnalysis = true,
            EnablePerformanceAnalysis = false
        };

        // Test Finding
        var finding = new Finding
        {
            Severity = FindingSeverity.Info,
            Message = "Test finding"
        };

        // Test AnalyzerResult
        var result = new AnalyzerResult
        {
            Success = true,
            AnalyzerId = "test-analyzer",
            Findings = new List<Finding> { finding }
        };

        Console.WriteLine($"Classes created successfully: {result.Success}, {result.Findings.Count} findings");
    }
}