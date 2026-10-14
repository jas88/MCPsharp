using MCPsharp.Models.Consolidated;
using Microsoft.CodeAnalysis;
using ConsolidatedLocation = MCPsharp.Models.Consolidated.Location;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    /// <summary>
    /// analyze_quality - Code quality analysis
    /// Consolidates: Duplicate detection, quality tools
    /// </summary>
    public async Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Quality metrics are computed over a file or directory: the focus file when given, else the project.
            // The scope is a label only; the metrics list selects which analyses run.
            var targetPath = string.IsNullOrWhiteSpace(request.FilePath) ? request.ProjectPath : request.FilePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return new QualityAnalysisResponse
                {
                    Scope = request.Scope.ToString(),
                    Error = ProjectPathRequiredError,
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
                };
            }

            var response = new QualityAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Duplicate code detection
            // Duplicate detection loads a project, so it always runs on the project and is then narrowed to the focus file
            if ((request.Metrics.Contains(AnalysisQualityMetric.Duplicates) || request.Metrics.Contains(AnalysisQualityMetric.All))
                && _duplicateDetector != null && !string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                var duplicates = await _duplicateDetector.DetectDuplicatesAsync(request.ProjectPath, new DuplicateDetectionOptions(), ct);
                var focusFile = string.IsNullOrWhiteSpace(request.FilePath) ? null : Path.GetFullPath(request.FilePath);
                response.Duplicates = duplicates.DuplicateGroups
                    .Where(d => focusFile == null || d.CodeBlocks.Any(b => string.Equals(Path.GetFullPath(b.FilePath), focusFile, PathContainment.Comparison)))
                    .Select(d => new DuplicateGroup
                    {
                        Type = d.DuplicationType.ToString(),
                        Similarity = d.SimilarityScore,
                        Files = d.CodeBlocks.Select(f => new DuplicateFile
                        {
                            FilePath = f.FilePath,
                            Locations = new List<MCPsharp.Models.Consolidated.Location>
                        {
                            new MCPsharp.Models.Consolidated.Location { FilePath = f.FilePath, Line = f.StartLine, Column = f.StartColumn }
                        },
                            Lines = f.EndLine - f.StartLine + 1,
                            MatchPercentage = d.SimilarityScore * 100
                        }).ToList(),
                        Description = $"Found {d.CodeBlocks.Count} duplicate blocks with similarity {d.SimilarityScore:P1}"
                    }).ToList();
            }

            // Complexity analysis
            if (request.Metrics.Contains(AnalysisQualityMetric.Complexity) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.Complexity = await AnalyzeComplexityAsync(targetPath, ct);
            }

            // Maintainability analysis
            if (request.Metrics.Contains(AnalysisQualityMetric.Maintainability) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.Maintainability = await AnalyzeMaintainabilityAsync(targetPath, ct);
            }

            // Test coverage
            if (request.Metrics.Contains(AnalysisQualityMetric.TestCoverage) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.TestCoverage = AnalyzeTestCoverage(targetPath);
            }

            // Code smells
            if (request.Metrics.Contains(AnalysisQualityMetric.CodeSmells) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.CodeSmells = await DetectCodeSmellsAsync(targetPath, ct);
            }

            // Quality score
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.QualityScore = CalculateQualityScore(response);
            }

            // Recommendations
            if (request.Options?.Include.HasFlag(IncludeOptions.Suggestions) == true)
            {
                response.Recommendations = GenerateQualityRecommendations(response);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingQualityFor(_logger, ex, request.Scope);

            return new QualityAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    private async Task<ComplexityAnalysis?> AnalyzeComplexityAsync(string targetPath, CancellationToken ct)
    {
        try
        {
            // Get all C# files in the target file or directory
            var files = EnumerateCSharpFiles(targetPath);
            var totalComplexity = 0;
            var fileComplexities = new List<FileComplexity>();

            foreach (var file in files.Take(20)) // Limit for performance
            {
                var complexity = await AnalyzeFileComplexityAsync(file, ct);
                if (complexity != null)
                {
                    totalComplexity += (int)complexity.CyclomaticComplexity;
                    fileComplexities.Add(complexity);
                }
            }

            var averageComplexity = fileComplexities.Count > 0 ? (double)totalComplexity / fileComplexities.Count : 0;
            var maxComplexity = fileComplexities.Count > 0 ? fileComplexities.Max(f => f.CyclomaticComplexity) : 0;

            return new ComplexityAnalysis
            {
                Scope = targetPath,
                TotalComplexity = totalComplexity,
                AverageComplexity = averageComplexity,
                MaxComplexity = maxComplexity,
                FileCount = fileComplexities.Count,
                ComplexFiles = fileComplexities.Where(f => f.CyclomaticComplexity > 10).Select(f => f.FilePath).ToList(),
                TotalMethods = fileComplexities.Sum(f => f.MethodComplexities?.Count ?? 0),
                ComplexMethods = fileComplexities.SelectMany(f => f.MethodComplexities ?? new List<MethodComplexity>())
                    .Where(m => m.CyclomaticComplexity > 10)
                    .Select(m => new ComplexityIssue
                    {
                        FilePath = m.Location.FilePath,
                        MethodName = m.MethodName,
                        Complexity = m.CyclomaticComplexity,
                        Location = m.Location
                    }).ToList()
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingComplexityFor(_logger, ex, targetPath);
        }

        return null;
    }

    private async Task<MaintainabilityAnalysis?> AnalyzeMaintainabilityAsync(string targetPath, CancellationToken ct)
    {
        try
        {
            var complexity = await AnalyzeComplexityAsync(targetPath, ct);
            if (complexity == null)
                return null;

            // Calculate maintainability index (simplified version)
            var averageMaintainability = Math.Max(0, 171 - 5.2 * Math.Log(complexity.AverageComplexity + 1) - 0.23 * complexity.AverageComplexity);

            return new MaintainabilityAnalysis
            {
                Scope = targetPath,
                MaintainabilityIndex = averageMaintainability,
                TechnicalDebt = Math.Max(0, 100 - averageMaintainability), // Simplified
                CodeQuality = averageMaintainability switch
                {
                    > 85 => "Excellent",
                    > 70 => "Good",
                    > 50 => "Fair",
                    _ => "Poor"
                },
                RefactoringPriority = averageMaintainability switch
                {
                    < 60 => 1.0,
                    < 80 => 0.5,
                    _ => 0.1
                },
                AverageMaintainabilityIndex = averageMaintainability,
                Issues = new List<MaintainabilityIssue>() // Empty list for now
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingMaintainabilityFor(_logger, ex, targetPath);
        }

        return null;
    }

    private TestCoverageAnalysis? AnalyzeTestCoverage(string targetPath)
    {
        try
        {
            var allFiles = EnumerateCSharpFiles(targetPath);
            var testFiles = allFiles.Where(f => Path.GetFileName(f).Contains("Test") ||
                                               Path.GetDirectoryName(f)?.Contains("Test") == true ||
                                               Path.GetDirectoryName(f)?.Contains("test") == true).ToList();

            var sourceFiles = allFiles.Except(testFiles).ToList();
            var coveragePercentage = sourceFiles.Count > 0 ? (double)testFiles.Count / sourceFiles.Count * 100 : 0;

            return new TestCoverageAnalysis
            {
                Scope = targetPath,
                TotalFiles = allFiles.Count,
                TestFiles = testFiles.Count,
                SourceFiles = sourceFiles.Count,
                CoveragePercentage = coveragePercentage,
                CoverageLevel = coveragePercentage switch
                {
                    > 80 => "Excellent",
                    > 60 => "Good",
                    > 40 => "Fair",
                    _ => "Poor"
                },
                OverallCoverage = coveragePercentage,
                Reports = new List<CoverageReport>() // Empty list for now
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingTestCoverageFor(_logger, ex, targetPath);
        }

        return null;
    }

    private async Task<List<CodeSmell>> DetectCodeSmellsAsync(string targetPath, CancellationToken ct)
    {
        try
        {
            var codeSmells = new List<CodeSmell>();
            var files = EnumerateCSharpFiles(targetPath);

            foreach (var file in files.Take(10)) // Limit for performance
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var lines = content.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    // Long method detection
                    if (line.Contains("public") || line.Contains("private"))
                    {
                        var methodLines = CountMethodLines(content, i);
                        if (methodLines > 50)
                        {
                            codeSmells.Add(new CodeSmell
                            {
                                Type = "Long Method",
                                Severity = "Warning",
                                Location = new ConsolidatedLocation
                                {
                                    FilePath = file,
                                    Line = i + 1,
                                    Column = 0
                                },
                                Description = $"Method exceeds 50 lines ({methodLines} lines)",
                                Suggestion = "Consider breaking this method into smaller methods"
                            });
                        }
                    }

                    // Large class detection
                    if (line.Contains("class "))
                    {
                        var classLines = CountClassLines(content, i);
                        if (classLines > 300)
                        {
                            codeSmells.Add(new CodeSmell
                            {
                                Type = "Large Class",
                                Severity = "Warning",
                                Location = new ConsolidatedLocation
                                {
                                    FilePath = file,
                                    Line = i + 1,
                                    Column = 0
                                },
                                Description = $"Class exceeds 300 lines ({classLines} lines)",
                                Suggestion = "Consider splitting this class into smaller classes"
                            });
                        }
                    }
                }
            }

            return codeSmells;
        }
        catch (Exception ex)
        {
            LogErrorDetectingCodeSmellsFor(_logger, ex, targetPath);
        }

        return new List<CodeSmell>();
    }

    private QualityScore? CalculateQualityScore(QualityAnalysisResponse analysis)
    {
        try
        {
            var score = 100.0;

            // Deduct points for duplicates
            if (analysis.Duplicates?.Count > 0)
                score -= analysis.Duplicates.Count * 2;

            // Deduct points for code smells
            if (analysis.CodeSmells?.Count > 0)
                score -= analysis.CodeSmells.Count * 3;

            // Factor in maintainability
            if (analysis.Maintainability?.MaintainabilityIndex > 0)
                score = Math.Min(score, analysis.Maintainability.MaintainabilityIndex);

            // Factor in test coverage
            if (analysis.TestCoverage?.CoveragePercentage > 0)
                score = score * (analysis.TestCoverage.CoveragePercentage / 100.0);

            score = Math.Max(0, Math.Min(100, score));

            return new QualityScore
            {
                OverallScore = score,
                CategoryScores = new Dictionary<string, double>
                {
                    ["Complexity"] = analysis.Complexity?.AverageComplexity > 0 ? Math.Max(0, 100 - analysis.Complexity.AverageComplexity * 2) : 100,
                    ["Maintainability"] = analysis.Maintainability?.MaintainabilityIndex ?? 100,
                    ["TestCoverage"] = analysis.TestCoverage?.CoveragePercentage ?? 0,
                    ["CodeSmells"] = Math.Max(0, 100 - (analysis.CodeSmells?.Count ?? 0) * 5)
                },
                Grade = score switch
                {
                    >= 90 => "A",
                    >= 80 => "B",
                    >= 70 => "C",
                    >= 60 => "D",
                    _ => "F"
                }
            };
        }
        catch (Exception ex)
        {
            LogErrorCalculatingQualityScore(_logger, ex);
        }

        return null;
    }

    private List<QualityRecommendation> GenerateQualityRecommendations(QualityAnalysisResponse analysis)
    {
        try
        {
            var recommendations = new List<QualityRecommendation>();

            // Recommendations based on duplicates
            if (analysis.Duplicates?.Count > 0)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Refactoring",
                    Description = $"Found {analysis.Duplicates.Count} instances of duplicate code",
                    Priority = 0.5, // Medium priority
                    Impact = 0.7, // Medium-high impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            // Recommendations based on code smells
            if (analysis.CodeSmells?.Count > 0)
            {
                var codeSmellRecs = analysis.CodeSmells
                    .GroupBy(cs => cs.Type)
                    .Select(g => new QualityRecommendation
                    {
                        Type = "Code Quality",
                        Description = $"Found {g.Count()} instances of {g.Key}",
                        Priority = g.Count() > 5 ? 0.8 : 0.5, // High/Medium priority
                        Impact = 0.6, // Medium impact
                        Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                    });
                recommendations.AddRange(codeSmellRecs);
            }

            // Recommendations based on test coverage
            if (analysis.TestCoverage?.CoveragePercentage < 80)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Testing",
                    Description = $"Current coverage: {analysis.TestCoverage.CoveragePercentage:F1}%",
                    Priority = analysis.TestCoverage.CoveragePercentage < 50 ? 0.8 : 0.5, // High/Medium priority
                    Impact = 0.7, // High impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            // Recommendations based on maintainability
            if (analysis.Maintainability?.MaintainabilityIndex < 70)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Architecture",
                    Description = $"Maintainability index: {analysis.Maintainability.MaintainabilityIndex:F1}",
                    Priority = 0.8, // High priority
                    Impact = 0.8, // High impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }
        catch (Exception ex)
        {
            LogErrorGeneratingQualityRecommendations(_logger, ex);
        }

        return new List<QualityRecommendation>();
    }
}
