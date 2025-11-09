using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Preview and preview-related functionality for bulk edit operations
/// </summary>
public partial class BulkEditService
{
    /// <inheritdoc />
    public async Task<PreviewResult> PreviewBulkChangesAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        var previewId = Guid.NewGuid().ToString();
        var generatedAt = DateTime.UtcNow;

        _logger?.LogInformation("Generating preview {PreviewId} for bulk edit operation", previewId);

        try
        {
            // Validate the request first
            var validationResult = await ValidateBulkEditAsync(request, cancellationToken);
            if (!validationResult.IsValid && validationResult.OverallSeverity >= ValidationSeverity.Error)
            {
                return new PreviewResult
                {
                    Success = false,
                    Error = "Validation failed: " + string.Join("; ", validationResult.Issues.Select(i => i.Description)),
                    FilePreviews = Array.Empty<FilePreviewResult>(),
                    Summary = "No files to preview",
                    Impact = new ImpactEstimate
                    {
                        OverallRisk = ChangeRiskLevel.Critical,
                        Risks = validationResult.Issues.Select(i => new RiskItem
                        {
                            Type = RiskType.Compilation,
                            Description = i.Description,
                            AffectedFiles = i.FilePath != null ? new[] { i.FilePath } : Array.Empty<string>(),
                            Severity = i.Severity switch
                            {
                                ValidationSeverity.Critical => RiskSeverity.Critical,
                                ValidationSeverity.Error => RiskSeverity.High,
                                ValidationSeverity.Warning => RiskSeverity.Medium,
                                _ => RiskSeverity.Low
                            },
                            Mitigation = i.SuggestedFix
                        }).ToArray(),
                        Complexity = new ComplexityEstimate
                        {
                            Score = 90,
                            Level = "Very High",
                            Factors = new[] { "Validation errors detected" },
                            EstimatedTime = TimeSpan.FromMinutes(30)
                        },
                        Recommendations = new[] { "Fix validation errors before proceeding" }
                    },
                    PreviewId = previewId,
                    GeneratedAt = generatedAt
                };
            }

            // Create a preview-only version of options
            var previewOptions = new BulkEditOptions
            {
                MaxParallelism = request.Options.MaxParallelism,
                CreateBackups = request.Options.CreateBackups,
                BackupDirectory = request.Options.BackupDirectory,
                PreviewMode = true,
                ValidateChanges = request.Options.ValidateChanges,
                MaxFileSize = request.Options.MaxFileSize,
                IncludeHiddenFiles = request.Options.IncludeHiddenFiles,
                StopOnFirstError = request.Options.StopOnFirstError,
                FileOperationTimeout = request.Options.FileOperationTimeout,
                RegexOptions = request.Options.RegexOptions,
                PreserveTimestamps = request.Options.PreserveTimestamps,
                ProgressReporter = request.Options.ProgressReporter,
                CancellationToken = request.Options.CancellationToken
            };

            var requestCopy = new BulkEditRequest
            {
                OperationType = request.OperationType,
                Files = request.Files,
                ExcludedFiles = request.ExcludedFiles,
                SearchPattern = request.SearchPattern,
                ReplacementText = request.ReplacementText,
                RegexPattern = request.RegexPattern,
                RegexReplacement = request.RegexReplacement,
                Condition = request.Condition,
                RefactorPattern = request.RefactorPattern,
                MultiFileEdits = request.MultiFileEdits,
                Options = previewOptions
            };

            // Resolve files that would be processed
            var filesToProcess = await ResolveFilePatterns(request.Files, previewOptions, cancellationToken);
            var filePreviews = new List<FilePreviewResult>();
            var totalChanges = 0;
            var linesAdded = 0;
            var linesRemoved = 0;
            var sizeChange = 0L;

            // Process each file to generate preview
            foreach (var filePath in filesToProcess)
            {
                var preview = await GenerateFilePreview(filePath, requestCopy, cancellationToken);
                filePreviews.Add(preview);

                if (preview.WouldChange)
                {
                    totalChanges += preview.ChangeCount;
                    // Estimate line changes and size impact
                    if (preview.PlannedChanges != null)
                    {
                        linesAdded += preview.PlannedChanges.Count(c => c.ChangeType == FileChangeType.Insert ||
                                                                      c.ChangeType == FileChangeType.Replace);
                        linesRemoved += preview.PlannedChanges.Count(c => c.ChangeType == FileChangeType.Delete ||
                                                                         c.ChangeType == FileChangeType.Replace);
                        sizeChange += preview.PlannedChanges.Sum(c =>
                            (c.NewText?.Length ?? 0) - (c.OriginalText?.Length ?? 0));
                    }
                }
            }

            var summary = new PreviewSummary
            {
                TotalFiles = filesToProcess.Count,
                FilesToChange = filePreviews.Count(p => p.WouldChange),
                FilesToSkip = filePreviews.Count(p => !p.WouldChange),
                TotalChanges = totalChanges,
                LinesAdded = linesAdded,
                LinesRemoved = linesRemoved,
                SizeChange = sizeChange
            };

            var impact = await EstimateImpactAsync(request, cancellationToken);

            return new PreviewResult
            {
                Success = true,
                FilePreviews = filePreviews,
                Summary = summary?.ToString() ?? string.Empty,
                Impact = impact,
                PreviewId = previewId,
                GeneratedAt = generatedAt
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to generate preview {PreviewId}", previewId);
            return new PreviewResult
            {
                Success = false,
                Error = ex.Message,
                FilePreviews = Array.Empty<FilePreviewResult>(),
                Summary = "Preview failed due to validation errors",
                Impact = new ImpactEstimate
                {
                    OverallRisk = ChangeRiskLevel.High,
                    Risks = new[]
                    {
                        new RiskItem
                        {
                            Type = RiskType.Other,
                            Description = "Preview generation failed",
                            AffectedFiles = Array.Empty<string>(),
                            Severity = RiskSeverity.High,
                            Mitigation = "Check file paths and patterns"
                        }
                    },
                    Complexity = new ComplexityEstimate
                    {
                        Score = 50,
                        Level = "Unknown",
                        Factors = new[] { "Preview generation error" },
                        EstimatedTime = TimeSpan.Zero
                    },
                    Recommendations = new[] { "Review error details and retry" }
                },
                PreviewId = previewId,
                GeneratedAt = generatedAt
            };
        }
    }

    private async Task<FilePreviewResult> GenerateFilePreview(
        string filePath,
        BulkEditRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var plannedChanges = new List<FileChange>();

            // Generate preview based on operation type
            switch (request.OperationType)
            {
                case BulkEditOperationType.BulkReplace:
                    if (!string.IsNullOrEmpty(request.RegexPattern))
                    {
                        var regex = new Regex(request.RegexPattern, request.Options.RegexOptions);
                        var matches = regex.Matches(content);
                        foreach (Match match in matches)
                        {
                            plannedChanges.Add(new FileChange
                            {
                                ChangeType = FileChangeType.Replace,
                                StartLine = GetLineNumber(content, match.Index),
                                StartColumn = GetColumnNumber(content, match.Index),
                                EndLine = GetLineNumber(content, match.Index + match.Length),
                                EndColumn = GetColumnNumber(content, match.Index + match.Length),
                                OriginalText = match.Value,
                                NewText = match.Result(request.RegexReplacement ?? "")
                            });
                        }
                    }
                    break;

                case BulkEditOperationType.ConditionalEdit:
                    if (request.Condition != null)
                    {
                        var conditionMet = await CheckCondition(content, request.Condition, filePath);
                        if (conditionMet && request.MultiFileEdits != null)
                        {
                            // Add changes from the operation
                            foreach (var operation in request.MultiFileEdits)
                            {
                                foreach (var edit in operation.Edits)
                                {
                                    // Convert TextEdit to FileChange
                                    plannedChanges.Add(ConvertTextEditToFileChange(edit));
                                }
                            }
                        }
                    }
                    break;
            }

            // Generate diff preview
            var diffPreview = plannedChanges.Count > 0 ? GenerateDiff(content, plannedChanges) : null;

            // Determine risk level
            var riskLevel = ChangeRiskLevel.Low;
            if (plannedChanges.Count > 10)
            {
                riskLevel = ChangeRiskLevel.Medium;
            }
            if (plannedChanges.Count > 50)
            {
                riskLevel = ChangeRiskLevel.High;
            }

            // Get file info for additional properties
            var fileInfo = new System.IO.FileInfo(filePath);
            var affectedLines = plannedChanges.Select(c => c.StartLine).Distinct().ToList();

            // Check if file is writable by trying to determine file attributes
            bool isWritable = true;
            try
            {
                var attributes = fileInfo.Attributes;
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    isWritable = false;
                }
            }
            catch
            {
                isWritable = false;
            }

            return new FilePreviewResult
            {
                FilePath = filePath,
                Success = true,
                Changes = plannedChanges,
                TotalChanges = plannedChanges.Count,
                RiskLevel = (RiskLevel)riskLevel,
                AffectedLines = affectedLines,
                IsWritable = isWritable,
                IsUnderSourceControl = false,
                FileSize = fileInfo.Length,
                GeneratedAt = DateTime.UtcNow,
                WouldChange = plannedChanges.Count > 0,
                DiffPreview = diffPreview,
                PlannedChanges = plannedChanges
            };
        }
        catch (Exception ex)
        {
            var fileInfo = new System.IO.FileInfo(filePath);

            // Check if file is writable by trying to determine file attributes
            bool isWritable = true;
            try
            {
                var attributes = fileInfo.Attributes;
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    isWritable = false;
                }
            }
            catch
            {
                isWritable = false;
            }

            return new FilePreviewResult
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message,
                Changes = Array.Empty<FileChange>(),
                TotalChanges = 0,
                RiskLevel = RiskLevel.High,
                AffectedLines = Array.Empty<int>(),
                IsWritable = isWritable,
                IsUnderSourceControl = false,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                GeneratedAt = DateTime.UtcNow,
                WouldChange = false,
                SkipReason = $"Failed to generate preview: {ex.Message}"
            };
        }
    }
}
