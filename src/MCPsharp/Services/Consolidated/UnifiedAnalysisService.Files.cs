using MCPsharp.Models.Consolidated;
using Microsoft.CodeAnalysis;
using ConsolidatedLocation = MCPsharp.Models.Consolidated.Location;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    private async Task<FileBasicInfo?> GetFileBasicInfoAsync(string filePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var fileInfo = new FileInfo(filePath);
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Basic language detection
            var language = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".cs" => "C#",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                ".py" => "Python",
                ".java" => "Java",
                ".cpp" or ".cxx" or ".cc" => "C++",
                ".c" => "C",
                ".json" => "JSON",
                ".xml" => "XML",
                ".yaml" or ".yml" => "YAML",
                ".md" => "Markdown",
                _ => "Unknown"
            };

            // Extract namespaces and types (basic implementation for C#)
            var namespaces = new List<string>();
            var types = new List<string>();

            if (language == "C#")
            {
                var namespaceMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"namespace\s+([^\s{;]+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                namespaces.AddRange(namespaceMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                var typeMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                types.AddRange(typeMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[2].Value));
            }

            return new FileBasicInfo
            {
                FilePath = filePath,
                Language = language,
                LineCount = lines.Length,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                Namespaces = namespaces.Distinct().ToList(),
                Types = types.Distinct().ToList()
            };
        }
        catch (Exception ex)
        {
            LogErrorGettingFileBasicInfo(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<FileStructure?> AnalyzeFileStructureAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var basicInfo = await GetFileBasicInfoAsync(filePath, ct);
            if (basicInfo == null) return null;

            var content = await File.ReadAllTextAsync(filePath, ct);
            var namespaces = new List<string>();
            var types = new List<FileTypeDefinition>();
            var members = new List<FileMemberDefinition>();
            var imports = new List<FileImport>();

            if (basicInfo.Language == "C#")
            {
                // Parse using regex for basic structure
                var namespaceMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"namespace\s+([^\s{]+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                namespaces.AddRange(namespaceMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                // Find type definitions
                var typePattern = @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)\s*(?::\s*([^{]+))?";
                var typeMatches = System.Text.RegularExpressions.Regex.Matches(content, typePattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                foreach (System.Text.RegularExpressions.Match match in typeMatches)
                {
                    types.Add(new FileTypeDefinition
                    {
                        Name = match.Groups[2].Value,
                        Kind = match.Groups[1].Value,
                        Location = new ConsolidatedLocation
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        BaseClass = match.Groups[3].Value?.Split(',').FirstOrDefault()?.Trim()
                    });
                }

                // Find using statements
                var usingPattern = @"using\s+([^\s;]+)";
                var usingMatches = System.Text.RegularExpressions.Regex.Matches(content, usingPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                foreach (System.Text.RegularExpressions.Match match in usingMatches)
                {
                    imports.Add(new FileImport
                    {
                        ImportPath = match.Groups[1].Value,
                        ImportType = "Using",
                        Location = new ConsolidatedLocation
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        IsUsed = true // Simplified - would need actual usage analysis
                    });
                }
            }

            return new FileStructure
            {
                Namespaces = namespaces.Distinct().ToList(),
                Types = types,
                Members = members,
                Imports = imports
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingFileStructureFor(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<FileComplexity?> AnalyzeFileComplexityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var linesOfCode = lines.Length;

            // Basic complexity analysis
            var methodPattern = @"(?:public|private|protected|internal)?\s*(?:static|async|virtual|override)?\s*[\w<>]+\s+(\w+)\s*\(";
            var methodMatches = System.Text.RegularExpressions.Regex.Matches(content, methodPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

            var methodComplexities = new List<MethodComplexity>();
            foreach (System.Text.RegularExpressions.Match match in methodMatches)
            {
                var methodName = match.Groups[1].Value;
                var lineNum = content.Substring(0, match.Index).Split('\n').Length;

                // Simplified complexity calculation
                var methodContent = ExtractMethodContent(content, match.Index);
                var complexity = CalculateCyclomaticComplexity(methodContent);

                methodComplexities.Add(new MethodComplexity
                {
                    MethodName = methodName,
                    Location = new ConsolidatedLocation
                    {
                        FilePath = filePath,
                        Line = lineNum,
                        Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                    },
                    CyclomaticComplexity = complexity
                });
            }

            var totalComplexity = methodComplexities.Sum(m => m.CyclomaticComplexity);
            var cognitiveComplexity = totalComplexity * 1.5; // Simplified cognitive complexity

            return new FileComplexity
            {
                FilePath = filePath,
                CyclomaticComplexity = totalComplexity,
                CognitiveComplexity = cognitiveComplexity,
                LinesOfCode = linesOfCode,
                MethodComplexities = methodComplexities
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingFileComplexityFor(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<FileDependencies?> AnalyzeFileDependenciesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var dependencies = new List<string>();
            var externalDependencies = new List<string>();

            if (string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                // Extract using statements
                var usingPattern = @"using\s+([^\s;]+)";
                var matches = System.Text.RegularExpressions.Regex.Matches(content, usingPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                dependencies.AddRange(matches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                // External dependencies are those that start with known external libraries
                externalDependencies = dependencies
                    .Where(d => d.StartsWith("System.", StringComparison.Ordinal) || d.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                                d.Contains('.') && !d.StartsWith("Project", StringComparison.Ordinal))
                    .ToList();
            }

            return new FileDependencies
            {
                FilePath = filePath,
                InternalDependencies = dependencies.Except(externalDependencies).Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "Using",
                    Locations = new List<MCPsharp.Models.Consolidated.Location> { new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList(),
                ExternalDependencies = externalDependencies.Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "External",
                    Locations = new List<MCPsharp.Models.Consolidated.Location> { new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingFileDependenciesFor(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<FileQuality?> AnalyzeFileQualityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var complexity = await AnalyzeFileComplexityAsync(filePath, ct);
            var dependencies = await AnalyzeFileDependenciesAsync(filePath, ct);

            if (complexity == null || dependencies == null) return null;

            // Calculate quality metrics
            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(complexity.LinesOfCode + 1)
                - 0.23 * complexity.CyclomaticComplexity - 16.2 * Math.Log(complexity.LinesOfCode + 1));

            var qualityScore = maintainabilityIndex;

            return new FileQuality
            {
                FilePath = filePath,
                QualityScore = qualityScore,
                Issues = new List<QualityIssue>(),
                Metrics = new List<QualityMetric>
                {
                    new QualityMetric { Name = "MaintainabilityIndex", Value = maintainabilityIndex, Unit = "index" },
                    new QualityMetric { Name = "ComplexityScore", Value = complexity.CyclomaticComplexity, Unit = "score" },
                    new QualityMetric { Name = "DependencyCount", Value = dependencies.InternalDependencies.Count + dependencies.ExternalDependencies.Count, Unit = "count" }
                }
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingFileQualityFor(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<List<FileIssue>?> DetectFileIssuesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var issues = new List<FileIssue>();
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n');

            // Check for common issues
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Long lines
                if (line.Length > 120)
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Style",
                        Severity = "Warning",
                        Message = "Line exceeds 120 characters",
                        Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = i + 1, Column = 120 }
                    });
                }

                // Unresolved task-marker comments
                if (line.Contains("TODO") || line.Contains("FIXME"))
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Documentation",
                        Severity = "Info",
                        Message = "Unresolved TODO or FIXME comment",
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = filePath,
                            Line = i + 1,
                            Column = line.Contains("TODO", StringComparison.Ordinal) ? line.IndexOf("TODO", StringComparison.Ordinal) : line.IndexOf("FIXME", StringComparison.Ordinal)
                        }
                    });
                }
            }

            return issues;
        }
        catch (Exception ex)
        {
            LogErrorDetectingFileIssuesFor(_logger, ex, filePath);
        }

        return null;
    }

    private async Task<List<FileSuggestion>?> GenerateFileSuggestionsAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var suggestions = new List<FileSuggestion>();
            var complexity = await AnalyzeFileComplexityAsync(filePath, ct);
            var dependencies = await AnalyzeFileDependenciesAsync(filePath, ct);

            if (complexity != null && complexity.CyclomaticComplexity > 10)
            {
                suggestions.Add(new FileSuggestion
                {
                    Type = "Refactoring",
                    Description = "Consider refactoring to reduce cyclomatic complexity",
                    Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            if (dependencies != null && (dependencies.InternalDependencies.Count + dependencies.ExternalDependencies.Count) > 20)
            {
                suggestions.Add(new FileSuggestion
                {
                    Type = "Dependency",
                    Description = "Consider reducing the number of dependencies",
                    Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            return suggestions;
        }
        catch (Exception ex)
        {
            LogErrorGeneratingFileSuggestionsFor(_logger, ex, filePath);
        }

        return null;
    }
}
