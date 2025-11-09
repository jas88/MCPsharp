using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;

namespace MCPsharp.Tests;

/// <summary>
/// Standalone program to analyze RDMP using MCPsharp's Roslyn analyzers
/// </summary>
public class AnalyzeRdmpProgram
{
    private static readonly string RdmpPath = "/Users/jas88/Developer/Github/RDMP";
    private static readonly string RdmpSolution = Path.Combine(RdmpPath, "DataManagementPlatform.sln");

    // Target specific projects for analysis
    private static readonly string[] TargetProjects = new[]
    {
        "Rdmp.Core/Rdmp.Core.csproj",
        "Rdmp.UI/Rdmp.UI.csproj",
        "RdmpDicom/Rdmp.Dicom/Rdmp.Dicom.csproj"
    };

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== MCPsharp Analyzer - RDMP Project Analysis ===\n");

        // Register MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            var instance = instances.OrderByDescending(i => i.Version).FirstOrDefault();
            if (instance != null)
            {
                MSBuildLocator.RegisterInstance(instance);
                Console.WriteLine($"Using MSBuild: {instance.Version} at {instance.MSBuildPath}\n");
            }
        }

        var analyzer = new AnalyzeRdmpProgram();
        await analyzer.RunAnalysisAsync();
    }

    private async Task RunAnalysisAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"Analyzing RDMP at: {RdmpPath}");
        Console.WriteLine($"Solution: {RdmpSolution}\n");

        // Load analyzers
        var analyzerLoader = new RoslynAnalyzerLoader(
            NullLogger<RoslynAnalyzerLoader>.Instance,
            NullLoggerFactory.Instance);

        Console.WriteLine("Loading Roslyn analyzers...");
        var loadedAnalyzers = await LoadAnalyzersAsync(analyzerLoader);
        Console.WriteLine($"Loaded {loadedAnalyzers.Count} analyzer assemblies\n");

        // Analyze each target project
        var allResults = new List<AnalysisResult>();

        foreach (var projectPath in TargetProjects)
        {
            var fullPath = Path.Combine(RdmpPath, projectPath);
            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"⚠️ Project not found: {fullPath}");
                continue;
            }

            Console.WriteLine($"\n{'=',60}");
            Console.WriteLine($"Analyzing: {projectPath}");
            Console.WriteLine($"{'=',60}");

            var result = await AnalyzeProjectAsync(fullPath, loadedAnalyzers);
            allResults.Add(result);
        }

        stopwatch.Stop();

        // Generate comprehensive report
        await GenerateReportAsync(allResults, stopwatch.Elapsed);
    }

    private async Task<List<DiagnosticAnalyzer>> LoadAnalyzersAsync(RoslynAnalyzerLoader loader)
    {
        var analyzers = new List<DiagnosticAnalyzer>();

        // Load Microsoft.CodeAnalysis.NetAnalyzers
        try
        {
            var netAnalyzers = await loader.LoadAnalyzersFromNuGetAsync(
                "Microsoft.CodeAnalysis.NetAnalyzers",
                "9.0.0",
                CancellationToken.None);

            analyzers.AddRange(netAnalyzers);
            Console.WriteLine($"✓ Loaded Microsoft.CodeAnalysis.NetAnalyzers: {netAnalyzers.Count} analyzers");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to load NetAnalyzers: {ex.Message}");
        }

        // Load Roslynator
        try
        {
            var roslynator = await loader.LoadAnalyzersFromNuGetAsync(
                "Roslynator.Analyzers",
                "4.12.10",
                CancellationToken.None);

            analyzers.AddRange(roslynator);
            Console.WriteLine($"✓ Loaded Roslynator.Analyzers: {roslynator.Count} analyzers");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to load Roslynator: {ex.Message}");
        }

        return analyzers;
    }

    private async Task<AnalysisResult> AnalyzeProjectAsync(
        string projectPath,
        List<DiagnosticAnalyzer> analyzers)
    {
        var result = new AnalysisResult { ProjectPath = projectPath };
        var sw = Stopwatch.StartNew();

        try
        {
            using var workspace = MSBuildWorkspace.Create();

            // Suppress MSBuild diagnostics
            workspace.WorkspaceFailed += (sender, e) =>
            {
                if (e.Diagnostic.Kind != WorkspaceDiagnosticKind.Failure)
                    return;
                Console.WriteLine($"  ⚠️ Workspace: {e.Diagnostic.Message}");
            };

            Console.WriteLine("  Loading project...");
            var project = await workspace.OpenProjectAsync(projectPath);

            Console.WriteLine($"  Project loaded: {project.Name}");
            Console.WriteLine($"  Documents: {project.Documents.Count()}");

            // Get compilation
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                result.Error = "Failed to get compilation";
                return result;
            }

            result.FileCount = project.Documents.Count();
            result.CompilationDiagnostics = compilation.GetDiagnostics()
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .ToList();

            Console.WriteLine($"  Compilation diagnostics: {result.CompilationDiagnostics.Count}");

            // Run analyzers
            Console.WriteLine($"  Running {analyzers.Count} analyzers...");

            var analyzerOptions = new CompilationWithAnalyzersOptions(
                options: new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: true);

            var compilationWithAnalyzers = compilation
                .WithAnalyzers(analyzers.ToImmutableArray(), analyzerOptions);

            var analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();

            result.AnalyzerDiagnostics = analyzerDiagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .ToList();

            sw.Stop();
            result.AnalysisTime = sw.Elapsed;

            Console.WriteLine($"  ✓ Analysis complete in {sw.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"  Issues found: {result.AnalyzerDiagnostics.Count}");

        }
        catch (Exception ex)
        {
            result.Error = ex.ToString();
            Console.WriteLine($"  ✗ Error: {ex.Message}");
        }

        return result;
    }

    private async Task GenerateReportAsync(List<AnalysisResult> results, TimeSpan totalTime)
    {
        var report = new StringBuilder();

        report.AppendLine("\n" + new string('=', 80));
        report.AppendLine("RDMP ANALYSIS REPORT - Generated by MCPsharp Roslyn Analyzers");
        report.AppendLine(new string('=', 80));
        report.AppendLine($"Analysis Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Total Analysis Time: {totalTime.TotalSeconds:F2} seconds");
        report.AppendLine($"Projects Analyzed: {results.Count(r => r.Error == null)}");
        report.AppendLine($"Projects Failed: {results.Count(r => r.Error != null)}");
        report.AppendLine();

        // Summary statistics
        var successfulResults = results.Where(r => r.Error == null).ToList();
        var totalFiles = successfulResults.Sum(r => r.FileCount);
        var totalCompilationIssues = successfulResults.Sum(r => r.CompilationDiagnostics.Count);
        var totalAnalyzerIssues = successfulResults.Sum(r => r.AnalyzerDiagnostics.Count);

        report.AppendLine("SUMMARY STATISTICS");
        report.AppendLine(new string('-', 80));
        report.AppendLine($"Total Files Analyzed: {totalFiles}");
        report.AppendLine($"Total Compilation Issues: {totalCompilationIssues}");
        report.AppendLine($"Total Analyzer Issues: {totalAnalyzerIssues}");
        report.AppendLine($"Average Issues per File: {(totalFiles > 0 ? (double)totalAnalyzerIssues / totalFiles : 0):F2}");
        report.AppendLine();

        // Per-project breakdown
        report.AppendLine("PER-PROJECT BREAKDOWN");
        report.AppendLine(new string('-', 80));

        foreach (var result in results)
        {
            report.AppendLine($"\nProject: {Path.GetFileName(result.ProjectPath)}");
            report.AppendLine($"  Path: {result.ProjectPath}");

            if (result.Error != null)
            {
                report.AppendLine($"  ✗ ERROR: {result.Error}");
                continue;
            }

            report.AppendLine($"  Files: {result.FileCount}");
            report.AppendLine($"  Analysis Time: {result.AnalysisTime.TotalSeconds:F2}s");
            report.AppendLine($"  Compilation Issues: {result.CompilationDiagnostics.Count}");
            report.AppendLine($"  Analyzer Issues: {result.AnalyzerDiagnostics.Count}");
            report.AppendLine($"  Issues per File: {(result.FileCount > 0 ? (double)result.AnalyzerDiagnostics.Count / result.FileCount : 0):F2}");
        }

        // Top issues by category
        report.AppendLine("\n\nTOP ISSUES BY CATEGORY");
        report.AppendLine(new string('-', 80));

        var allIssues = successfulResults
            .SelectMany(r => r.AnalyzerDiagnostics)
            .ToList();

        var issuesByCategory = allIssues
            .GroupBy(d => d.Descriptor.Category)
            .OrderByDescending(g => g.Count())
            .Take(10);

        foreach (var category in issuesByCategory)
        {
            report.AppendLine($"\n{category.Key}: {category.Count()} issues");

            var topInCategory = category
                .GroupBy(d => d.Id)
                .OrderByDescending(g => g.Count())
                .Take(5);

            foreach (var issue in topInCategory)
            {
                var sample = issue.First();
                report.AppendLine($"  {sample.Id} ({issue.Count()}): {sample.Descriptor.Title}");
                report.AppendLine($"    Severity: {sample.Severity}");
            }
        }

        // Top 20 most frequent issues
        report.AppendLine("\n\nTOP 20 MOST FREQUENT ISSUES");
        report.AppendLine(new string('-', 80));

        var topIssues = allIssues
            .GroupBy(d => d.Id)
            .OrderByDescending(g => g.Count())
            .Take(20);

        int rank = 1;
        foreach (var issueGroup in topIssues)
        {
            var sample = issueGroup.First();
            report.AppendLine($"\n#{rank}. {sample.Id} - {sample.Descriptor.Title}");
            report.AppendLine($"  Count: {issueGroup.Count()}");
            report.AppendLine($"  Severity: {sample.Severity}");
            report.AppendLine($"  Category: {sample.Descriptor.Category}");
            report.AppendLine($"  Description: {sample.Descriptor.Description}");

            // Show a sample location
            var withLocation = issueGroup.FirstOrDefault(d => d.Location.IsInSource);
            if (withLocation != null)
            {
                var lineSpan = withLocation.Location.GetLineSpan();
                report.AppendLine($"  Sample: {Path.GetFileName(lineSpan.Path)}:{lineSpan.StartLinePosition.Line + 1}");
            }

            rank++;
        }

        // Severity breakdown
        report.AppendLine("\n\nISSUES BY SEVERITY");
        report.AppendLine(new string('-', 80));

        var bySeverity = allIssues
            .GroupBy(d => d.Severity)
            .OrderByDescending(g => g.Key);

        foreach (var severity in bySeverity)
        {
            report.AppendLine($"{severity.Key}: {severity.Count()} issues");
        }

        // Save report
        var reportPath = Path.Combine(
            "/Users/jas88/Developer/Github/MCPsharp/docs",
            "rdmp-analysis-report.md");

        await File.WriteAllTextAsync(reportPath, report.ToString());

        Console.WriteLine(report.ToString());
        Console.WriteLine($"\n✓ Full report saved to: {reportPath}");
    }

    private class AnalysisResult
    {
        public string ProjectPath { get; set; } = "";
        public int FileCount { get; set; }
        public List<Diagnostic> CompilationDiagnostics { get; set; } = new();
        public List<Diagnostic> AnalyzerDiagnostics { get; set; } = new();
        public TimeSpan AnalysisTime { get; set; }
        public string? Error { get; set; }
    }
}
