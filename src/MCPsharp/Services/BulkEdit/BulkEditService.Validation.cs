using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Validation and impact analysis methods for bulk edit operations
/// </summary>
public partial class BulkEditService
{
    /// <inheritdoc />
    public async Task<ValidationResult> ValidateBulkEditAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        try
        {
            // Validate file patterns
            foreach (var pattern in request.Files)
            {
                try
                {
                    var files = await ResolveFilePatterns(new[] { pattern }, request.Options, cancellationToken);
                    if (files.Count == 0)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = ValidationIssueType.InvalidPath,
                            Severity = ValidationSeverity.Warning,
                            Description = $"No files found matching pattern: {pattern}",
                            IsBlocking = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue
                    {
                        Type = ValidationIssueType.InvalidPath,
                        Severity = ValidationSeverity.Error,
                        Description = $"Invalid file pattern '{pattern}': {ex.Message}",
                        IsBlocking = true
                    });
                }
            }

            // Validate regex patterns if provided
            if (!string.IsNullOrEmpty(request.RegexPattern))
            {
                try
                {
                    _ = new Regex(request.RegexPattern, request.Options.RegexOptions);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue
                    {
                        Type = ValidationIssueType.RegexError,
                        Severity = ValidationSeverity.Error,
                        Description = $"Invalid regex pattern: {ex.Message}",
                        IsBlocking = true
                    });
                }
            }

            // Validate operation-specific requirements
            switch (request.OperationType)
            {
                case BulkEditOperationType.BulkReplace:
                    if (string.IsNullOrEmpty(request.RegexPattern))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = ValidationIssueType.ConfigurationIssue,
                            Severity = ValidationSeverity.Error,
                            Description = "Regex pattern is required for bulk replace operations",
                            IsBlocking = true
                        });
                    }
                    break;

                case BulkEditOperationType.ConditionalEdit:
                    if (request.Condition == null)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = ValidationIssueType.ConfigurationIssue,
                            Severity = ValidationSeverity.Error,
                            Description = "Condition is required for conditional edit operations",
                            IsBlocking = true
                        });
                    }
                    break;

                case BulkEditOperationType.BatchRefactor:
                    if (request.RefactorPattern == null)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = ValidationIssueType.ConfigurationIssue,
                            Severity = ValidationSeverity.Error,
                            Description = "Refactor pattern is required for batch refactor operations",
                            IsBlocking = true
                        });
                    }
                    break;

                case BulkEditOperationType.MultiFileEdit:
                    if (request.MultiFileEdits == null || request.MultiFileEdits.Count == 0)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = ValidationIssueType.ConfigurationIssue,
                            Severity = ValidationSeverity.Error,
                            Description = "Multi-file edits are required for multi-file edit operations",
                            IsBlocking = true
                        });
                    }
                    break;
            }

            // Check for potential security issues
            if (request.Files.Any(f => f.Contains("..") || Path.IsPathRooted(f) && !f.StartsWith("/")))
            {
                issues.Add(new ValidationIssue
                {
                    Type = ValidationIssueType.SecurityIssue,
                    Severity = ValidationSeverity.Warning,
                    Description = "File patterns contain potentially unsafe paths",
                    IsBlocking = false,
                    SuggestedFix = "Review file patterns to ensure they target intended files only"
                });
            }

            return new ValidationResult
            {
                IsValid = !issues.Any(i => i.IsBlocking),
                Issues = issues,
                OverallSeverity = issues.Any(i => i.Severity == ValidationSeverity.Critical) ? ValidationSeverity.Critical :
                                   issues.Any(i => i.Severity == ValidationSeverity.Error) ? ValidationSeverity.Error :
                                   issues.Any(i => i.Severity == ValidationSeverity.Warning) ? ValidationSeverity.Warning :
                                   issues.Any() ? ValidationSeverity.Info : ValidationSeverity.None,
                Summary = issues.Count > 0
                    ? $"Found {issues.Count(i => i.Severity >= ValidationSeverity.Error)} errors and {issues.Count(i => i.Severity == ValidationSeverity.Warning)} warnings"
                    : "Validation passed successfully"
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Issues = new[]
                {
                    new ValidationIssue
                    {
                        Type = ValidationIssueType.Other,
                        Severity = ValidationSeverity.Critical,
                        Description = $"Validation failed with error: {ex.Message}",
                        IsBlocking = true
                    }
                },
                OverallSeverity = ValidationSeverity.Critical,
                Summary = "Validation process failed"
            };
        }
    }

    /// <inheritdoc />
    public async Task<ImpactEstimate> EstimateImpactAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var risks = new List<RiskItem>();
            var complexityFactors = new List<string>();
            var recommendations = new List<string>();

            // Get file statistics
            var stats = await GetFileStatisticsAsync(request.Files, cancellationToken);

            // Base complexity on file count and size
            var complexityScore = Math.Min(100, stats.TotalFiles / 10 + (int)(stats.TotalSize / (1024 * 1024)));
            var riskLevel = ChangeRiskLevel.Low;

            // Analyze operation type risks
            switch (request.OperationType)
            {
                case BulkEditOperationType.BulkReplace:
                    if (!string.IsNullOrEmpty(request.RegexPattern) && request.RegexPattern.Contains(".*"))
                    {
                        risks.Add(new RiskItem
                        {
                            Type = RiskType.Compilation,
                            Description = "Broad regex pattern may match unintended content",
                            AffectedFiles = request.Files,
                            Severity = RiskSeverity.Medium,
                            Mitigation = "Test regex pattern on small subset first"
                        });
                        complexityScore += 20;
                    }
                    break;

                case BulkEditOperationType.BatchRefactor:
                    risks.Add(new RiskItem
                    {
                        Type = RiskType.Compilation,
                        Description = "Refactoring operations may break code compilation",
                        AffectedFiles = request.Files,
                        Severity = RiskSeverity.High,
                        Mitigation = "Run compilation check after refactoring"
                    });
                    complexityScore += 30;
                    riskLevel = ChangeRiskLevel.Medium;
                    break;

                case BulkEditOperationType.MultiFileEdit:
                    if (request.MultiFileEdits?.Count > 5)
                    {
                        risks.Add(new RiskItem
                        {
                            Type = RiskType.Dependencies,
                            Description = "Complex multi-file operations may have dependency issues",
                            AffectedFiles = request.Files,
                            Severity = RiskSeverity.Medium,
                            Mitigation = "Verify operation dependencies and order"
                        });
                        complexityScore += 25;
                    }
                    break;
            }

            // File size risks
            if (stats.OversizedFiles.Any())
            {
                risks.Add(new RiskItem
                {
                    Type = RiskType.Performance,
                    Description = $"Found {stats.OversizedFiles.Count} files that may be too large to process efficiently",
                    AffectedFiles = stats.OversizedFiles,
                    Severity = RiskSeverity.Medium,
                    Mitigation = "Consider splitting large files or processing them separately"
                });
                complexityScore += 15;
            }

            // File access risks
            if (stats.InaccessibleFiles.Any())
            {
                risks.Add(new RiskItem
                {
                    Type = RiskType.FileAccessIssue,
                    Description = $"Found {stats.InaccessibleFiles.Count} files that cannot be accessed",
                    AffectedFiles = stats.InaccessibleFiles,
                    Severity = RiskSeverity.High,
                    Mitigation = "Check file permissions and ensure files are not locked"
                });
                complexityScore += 10;
                riskLevel = ChangeRiskLevel.High;
            }

            // Determine overall risk level
            if (complexityScore >= 80 || risks.Any(r => r.Severity == RiskSeverity.Critical))
            {
                riskLevel = ChangeRiskLevel.Critical;
            }
            else if (complexityScore >= 60 || risks.Any(r => r.Severity == RiskSeverity.High))
            {
                riskLevel = ChangeRiskLevel.High;
            }
            else if (complexityScore >= 40 || risks.Any(r => r.Severity == RiskSeverity.Medium))
            {
                riskLevel = ChangeRiskLevel.Medium;
            }

            // Add complexity factors
            if (stats.TotalFiles > 100) complexityFactors.Add("Large number of files");
            if (stats.TotalSize > 50 * 1024 * 1024) complexityFactors.Add("Large total file size");
            if (request.Options.CreateBackups) complexityFactors.Add("Backup creation required");
            if (request.Options.ValidateChanges) complexityFactors.Add("Change validation enabled");

            // Add recommendations
            if (riskLevel >= ChangeRiskLevel.High)
            {
                recommendations.Add("Create a backup of your entire project before proceeding");
                recommendations.Add("Consider running on a small subset first");
            }

            if (stats.TotalFiles > 50)
            {
                recommendations.Add("Process files in smaller batches for better control");
            }

            recommendations.Add("Review the preview carefully before applying changes");

            // Estimate processing time
            var estimatedTime = TimeSpan.FromSeconds(stats.TotalFiles * 0.5 + stats.TotalSize / (1024 * 1024));

            var complexityLevel = complexityScore switch
            {
                >= 80 => "Very High",
                >= 60 => "High",
                >= 40 => "Medium",
                >= 20 => "Low",
                _ => "Very Low"
            };

            return new ImpactEstimate
            {
                OverallRisk = riskLevel,
                Risks = risks,
                Complexity = new ComplexityEstimate
                {
                    Score = Math.Min(100, complexityScore),
                    Level = complexityLevel,
                    Factors = complexityFactors.Any() ? complexityFactors : new[] { "Standard complexity" },
                    EstimatedTime = estimatedTime
                },
                Recommendations = recommendations.Any() ? recommendations : new[] { "Operation appears safe to proceed" }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to estimate impact");
            return new ImpactEstimate
            {
                OverallRisk = ChangeRiskLevel.High,
                Risks = new[]
                {
                    new RiskItem
                    {
                        Type = RiskType.Other,
                        Description = "Failed to estimate impact due to an error",
                        AffectedFiles = Array.Empty<string>(),
                        Severity = RiskSeverity.High,
                        Mitigation = "Check file patterns and try again"
                    }
                },
                Complexity = new ComplexityEstimate
                {
                    Score = 50,
                    Level = "Unknown",
                    Factors = new[] { "Impact estimation failed" },
                    EstimatedTime = TimeSpan.FromMinutes(5)
                },
                Recommendations = new[] { "Review configuration and retry" }
            };
        }
    }

    /// <inheritdoc />
    public async Task<FileStatistics> GetFileStatisticsAsync(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new BulkEditOptions(); // Use default options
            var resolvedFiles = await ResolveFilePatterns(files, options, cancellationToken);

            var fileSizes = new List<long>();
            var lineCounts = new List<int>();
            var fileTypes = new Dictionary<string, int>();
            var oversizedFiles = new List<string>();
            var inaccessibleFiles = new List<string>();

            // First, check which original files don't exist
            foreach (var originalFile in files)
            {
                if (!File.Exists(originalFile) && !Directory.Exists(originalFile))
                {
                    inaccessibleFiles.Add(originalFile);
                }
            }

            foreach (var filePath in resolvedFiles)
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(filePath);

                    if (fileInfo.Length > options.MaxFileSize)
                    {
                        oversizedFiles.Add(filePath);
                        continue;
                    }

                    fileSizes.Add(fileInfo.Length);

                    // Count lines
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var lineCount = content.Split('\n').Length;
                    lineCounts.Add(lineCount);

                    // Track file types
                    var extension = fileInfo.Extension.ToLowerInvariant();
                    fileTypes[extension] = fileTypes.GetValueOrDefault(extension, 0) + 1;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to analyze file {FilePath}", filePath);
                    inaccessibleFiles.Add(filePath);
                }
            }

            var totalSize = fileSizes.Sum();
            var averageSize = fileSizes.Any() ? fileSizes.Average() : 0;
            var largestSize = fileSizes.Any() ? fileSizes.Max() : 0;
            var smallestSize = fileSizes.Any() ? fileSizes.Min() : 0;
            var totalLines = lineCounts.Sum();

            // Estimate processing time
            var estimatedProcessingTime = TimeSpan.FromSeconds(
                resolvedFiles.Count * 0.5 + totalSize / (1024 * 1024) * 2);

            // Recommend batch size based on file count and sizes
            var recommendedBatchSize = Math.Min(50, Math.Max(5, Environment.ProcessorCount * 2));

            return new FileStatistics
            {
                TotalFiles = resolvedFiles.Count,
                TotalSize = totalSize,
                AverageFileSize = (long)averageSize,
                LargestFileSize = largestSize,
                SmallestFileSize = smallestSize,
                TotalLines = totalLines,
                FileTypes = fileTypes,
                OversizedFiles = oversizedFiles,
                InaccessibleFiles = inaccessibleFiles,
                EstimatedProcessingTime = estimatedProcessingTime,
                RecommendedBatchSize = recommendedBatchSize
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get file statistics");
            return new FileStatistics
            {
                TotalFiles = 0,
                TotalSize = 0,
                AverageFileSize = 0,
                LargestFileSize = 0,
                SmallestFileSize = 0,
                TotalLines = 0,
                FileTypes = new Dictionary<string, int>(),
                OversizedFiles = Array.Empty<string>(),
                InaccessibleFiles = files,
                EstimatedProcessingTime = TimeSpan.Zero,
                RecommendedBatchSize = 10
            };
        }
    }
}
