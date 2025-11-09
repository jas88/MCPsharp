using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for generating analysis reports in various formats
/// </summary>
public class AnalysisReportGenerator
{
    private readonly ILogger<AnalysisReportGenerator> _logger;

    public AnalysisReportGenerator(ILogger<AnalysisReportGenerator>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalysisReportGenerator>.Instance;
    }

    /// <summary>
    /// Generate a report from analysis results
    /// </summary>
    public async Task<ReportResult> GenerateReportAsync(
        ImmutableArray<AnalysisResult> results,
        ReportFormat format,
        string outputPath,
        ReportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating {Format} report to {OutputPath}", format, outputPath);

            options ??= new ReportOptions();

            var reportContent = format switch
            {
                ReportFormat.Html => GenerateHtmlReport(results, options),
                ReportFormat.Markdown => GenerateMarkdownReport(results, options),
                ReportFormat.Json => GenerateJsonReport(results, options),
                ReportFormat.Csv => GenerateCsvReport(results, options),
                _ => throw new ArgumentException($"Unsupported report format: {format}")
            };

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, reportContent, cancellationToken);

            var statistics = CalculateStatistics(results);

            _logger.LogInformation("Report generated successfully: {OutputPath}", outputPath);

            return new ReportResult
            {
                Success = true,
                FilePath = outputPath,
                Format = format,
                Statistics = statistics,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating report");
            return new ReportResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Format = format,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }

    private string GenerateHtmlReport(ImmutableArray<AnalysisResult> results, ReportOptions options)
    {
        var sb = new StringBuilder();
        var statistics = CalculateStatistics(results);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>Analysis Report - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }");
        sb.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background-color: white; padding: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
        sb.AppendLine("        h1, h2, h3 { color: #333; }");
        sb.AppendLine("        .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }");
        sb.AppendLine("        .stat-card { background-color: #f8f9fa; padding: 15px; border-radius: 5px; border-left: 4px solid #007bff; }");
        sb.AppendLine("        .stat-value { font-size: 2em; font-weight: bold; color: #007bff; }");
        sb.AppendLine("        .stat-label { color: #666; font-size: 0.9em; }");
        sb.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
        sb.AppendLine("        th, td { padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }");
        sb.AppendLine("        th { background-color: #f8f9fa; font-weight: bold; }");
        sb.AppendLine("        .severity-error { color: #dc3545; font-weight: bold; }");
        sb.AppendLine("        .severity-warning { color: #ffc107; font-weight: bold; }");
        sb.AppendLine("        .severity-info { color: #17a2b8; }");
        sb.AppendLine("        .severity-critical { color: #8b0000; font-weight: bold; }");
        sb.AppendLine("        .issue-card { background-color: #f8f9fa; padding: 15px; margin: 10px 0; border-radius: 5px; border-left: 4px solid #666; }");
        sb.AppendLine("        .issue-card.error { border-left-color: #dc3545; }");
        sb.AppendLine("        .issue-card.warning { border-left-color: #ffc107; }");
        sb.AppendLine("        .issue-card.info { border-left-color: #17a2b8; }");
        sb.AppendLine("        .issue-card.critical { border-left-color: #8b0000; }");
        sb.AppendLine("        .file-path { font-family: monospace; background-color: #e9ecef; padding: 2px 6px; border-radius: 3px; }");
        sb.AppendLine("        .location { color: #666; font-size: 0.9em; }");
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine($"        <h1>Code Analysis Report</h1>");
        sb.AppendLine($"        <p>Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

        // Statistics
        sb.AppendLine("        <h2>Summary Statistics</h2>");
        sb.AppendLine("        <div class=\"stats\">");
        sb.AppendLine($"            <div class=\"stat-card\"><div class=\"stat-value\">{statistics.TotalFiles}</div><div class=\"stat-label\">Files Analyzed</div></div>");
        sb.AppendLine($"            <div class=\"stat-card\"><div class=\"stat-value\">{statistics.TotalIssues}</div><div class=\"stat-label\">Total Issues</div></div>");
        sb.AppendLine($"            <div class=\"stat-card\"><div class=\"stat-value\" style=\"color: #dc3545;\">{statistics.ErrorCount}</div><div class=\"stat-label\">Errors</div></div>");
        sb.AppendLine($"            <div class=\"stat-card\"><div class=\"stat-value\" style=\"color: #ffc107;\">{statistics.WarningCount}</div><div class=\"stat-label\">Warnings</div></div>");
        sb.AppendLine($"            <div class=\"stat-card\"><div class=\"stat-value\" style=\"color: #17a2b8;\">{statistics.InfoCount}</div><div class=\"stat-label\">Info</div></div>");
        sb.AppendLine("        </div>");

        // Issues by severity
        if (options.IncludeIssues)
        {
            sb.AppendLine("        <h2>Issues by Severity</h2>");

            var issuesBySeverity = results
                .SelectMany(r => r.Issues)
                .GroupBy(i => i.Severity)
                .OrderByDescending(g => g.Key);

            foreach (var severityGroup in issuesBySeverity)
            {
                sb.AppendLine($"        <h3>{severityGroup.Key} ({severityGroup.Count()})</h3>");

                foreach (var issue in severityGroup.Take(options.MaxIssuesPerCategory))
                {
                    var cssClass = severityGroup.Key.ToString().ToLowerInvariant();
                    sb.AppendLine($"        <div class=\"issue-card {cssClass}\">");
                    sb.AppendLine($"            <strong>{issue.Title}</strong>");
                    sb.AppendLine($"            <p>{issue.Description}</p>");
                    sb.AppendLine($"            <div class=\"location\">");
                    sb.AppendLine($"                <span class=\"file-path\">{issue.FilePath}</span> ");
                    sb.AppendLine($"                Line {issue.LineNumber}, Column {issue.ColumnNumber}");
                    sb.AppendLine($"            </div>");
                    if (!string.IsNullOrEmpty(issue.HelpLink))
                    {
                        sb.AppendLine($"            <p><a href=\"{issue.HelpLink}\" target=\"_blank\">More Info</a></p>");
                    }
                    sb.AppendLine("        </div>");
                }

                if (severityGroup.Count() > options.MaxIssuesPerCategory)
                {
                    sb.AppendLine($"        <p><em>... and {severityGroup.Count() - options.MaxIssuesPerCategory} more {severityGroup.Key} issues</em></p>");
                }
            }
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private string GenerateMarkdownReport(ImmutableArray<AnalysisResult> results, ReportOptions options)
    {
        var sb = new StringBuilder();
        var statistics = CalculateStatistics(results);

        sb.AppendLine("# Code Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        sb.AppendLine("## Summary Statistics");
        sb.AppendLine();
        sb.AppendLine($"- **Files Analyzed:** {statistics.TotalFiles}");
        sb.AppendLine($"- **Total Issues:** {statistics.TotalIssues}");
        sb.AppendLine($"- **Errors:** {statistics.ErrorCount}");
        sb.AppendLine($"- **Warnings:** {statistics.WarningCount}");
        sb.AppendLine($"- **Info:** {statistics.InfoCount}");
        sb.AppendLine($"- **Critical:** {statistics.CriticalCount}");
        sb.AppendLine();

        // Issues by category
        if (options.IncludeIssues)
        {
            sb.AppendLine("## Issues by Category");
            sb.AppendLine();

            var issuesByCategory = results
                .SelectMany(r => r.Issues)
                .GroupBy(i => i.Category)
                .OrderByDescending(g => g.Count());

            foreach (var categoryGroup in issuesByCategory)
            {
                sb.AppendLine($"### {categoryGroup.Key} ({categoryGroup.Count()})");
                sb.AppendLine();

                foreach (var issue in categoryGroup.Take(options.MaxIssuesPerCategory))
                {
                    sb.AppendLine($"**{issue.Severity}:** {issue.Title}");
                    sb.AppendLine();
                    sb.AppendLine($"```");
                    sb.AppendLine($"File: {issue.FilePath}");
                    sb.AppendLine($"Location: Line {issue.LineNumber}, Column {issue.ColumnNumber}");
                    sb.AppendLine($"Rule: {issue.RuleId}");
                    sb.AppendLine($"```");
                    sb.AppendLine();
                    sb.AppendLine($"{issue.Description}");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(issue.HelpLink))
                    {
                        sb.AppendLine($"[More Info]({issue.HelpLink})");
                        sb.AppendLine();
                    }

                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                if (categoryGroup.Count() > options.MaxIssuesPerCategory)
                {
                    sb.AppendLine($"*... and {categoryGroup.Count() - options.MaxIssuesPerCategory} more {categoryGroup.Key} issues*");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private string GenerateJsonReport(ImmutableArray<AnalysisResult> results, ReportOptions options)
    {
        var report = new
        {
            generatedAt = DateTime.UtcNow,
            statistics = CalculateStatistics(results),
            results = results.Select(r => new
            {
                filePath = r.FilePath,
                analyzerId = r.AnalyzerId,
                success = r.Success,
                errorMessage = r.ErrorMessage,
                issueCount = r.Issues.Length,
                issues = options.IncludeIssues ? r.Issues.Select(i => new
                {
                    id = i.Id,
                    ruleId = i.RuleId,
                    title = i.Title,
                    description = i.Description,
                    severity = i.Severity.ToString(),
                    category = i.Category.ToString(),
                    filePath = i.FilePath,
                    line = i.LineNumber,
                    column = i.ColumnNumber,
                    helpLink = i.HelpLink
                }).ToArray() : Array.Empty<object>()
            }).ToArray()
        };

        return System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private string GenerateCsvReport(ImmutableArray<AnalysisResult> results, ReportOptions options)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("FilePath,RuleId,Severity,Category,Line,Column,Title,Description");

        // Data rows
        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                sb.AppendLine($"\"{EscapeCsv(issue.FilePath)}\",\"{EscapeCsv(issue.RuleId)}\",\"{issue.Severity}\",\"{issue.Category}\",{issue.LineNumber},{issue.ColumnNumber},\"{EscapeCsv(issue.Title)}\",\"{EscapeCsv(issue.Description)}\"");
            }
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\"", "\"\"");
    }

    private ReportStatistics CalculateStatistics(ImmutableArray<AnalysisResult> results)
    {
        var allIssues = results.SelectMany(r => r.Issues).ToList();

        return new ReportStatistics
        {
            TotalFiles = results.Length,
            SuccessfulAnalyses = results.Count(r => r.Success),
            FailedAnalyses = results.Count(r => !r.Success),
            TotalIssues = allIssues.Count,
            ErrorCount = allIssues.Count(i => i.Severity == IssueSeverity.Error),
            WarningCount = allIssues.Count(i => i.Severity == IssueSeverity.Warning),
            InfoCount = allIssues.Count(i => i.Severity == IssueSeverity.Info),
            CriticalCount = allIssues.Count(i => i.Severity == IssueSeverity.Critical),
            IssuesByCategory = allIssues
                .GroupBy(i => i.Category)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            TopIssues = allIssues
                .GroupBy(i => i.RuleId)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}

/// <summary>
/// Report format options
/// </summary>
public enum ReportFormat
{
    Html,
    Markdown,
    Json,
    Csv
}

/// <summary>
/// Options for report generation
/// </summary>
public record ReportOptions
{
    public bool IncludeIssues { get; init; } = true;
    public bool IncludeStatistics { get; init; } = true;
    public int MaxIssuesPerCategory { get; init; } = 100;
    public bool IncludeSuccessfulAnalyses { get; init; } = true;
}

/// <summary>
/// Result of report generation
/// </summary>
public record ReportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public ReportFormat Format { get; init; }
    public ReportStatistics? Statistics { get; init; }
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Report statistics
/// </summary>
public record ReportStatistics
{
    public int TotalFiles { get; init; }
    public int SuccessfulAnalyses { get; init; }
    public int FailedAnalyses { get; init; }
    public int TotalIssues { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int CriticalCount { get; init; }
    public Dictionary<string, int> IssuesByCategory { get; init; } = new();
    public Dictionary<string, int> TopIssues { get; init; } = new();
}
