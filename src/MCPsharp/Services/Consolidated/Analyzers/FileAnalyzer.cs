using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using System.Text.RegularExpressions;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes file-level code elements including structure, complexity, dependencies, and quality.
/// </summary>
public class FileAnalyzer
{
    private readonly ILogger<FileAnalyzer> _logger;

    public FileAnalyzer(ILogger<FileAnalyzer>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FileAnalyzer>.Instance;
    }

    public async Task<FileBasicInfo?> GetFileBasicInfoAsync(string filePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var fileInfo = new FileInfo(filePath);
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

            var namespaces = new List<string>();
            var types = new List<string>();

            if (language == "C#")
            {
                var namespaceMatches = Regex.Matches(content, @"namespace\s+([^\s{;]+)");
                namespaces.AddRange(namespaceMatches.Cast<Match>().Select(m => m.Groups[1].Value));

                var typeMatches = Regex.Matches(content, @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)");
                types.AddRange(typeMatches.Cast<Match>().Select(m => m.Groups[2].Value));
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
            _logger.LogError(ex, "Error getting file basic info for {File}", filePath);
        }

        return null;
    }

    public async Task<FileStructure?> AnalyzeFileStructureAsync(string filePath, CancellationToken ct)
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
                var namespaceMatches = Regex.Matches(content, @"namespace\s+([^\s{]+)");
                namespaces.AddRange(namespaceMatches.Cast<Match>().Select(m => m.Groups[1].Value));

                var typePattern = @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)\s*(?::\s*([^{]+))?";
                var typeMatches = Regex.Matches(content, typePattern);
                foreach (Match match in typeMatches)
                {
                    types.Add(new FileTypeDefinition
                    {
                        Name = match.Groups[2].Value,
                        Kind = match.Groups[1].Value,
                        Location = new Location
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        BaseClass = match.Groups[3].Value?.Split(',').FirstOrDefault()?.Trim()
                    });
                }

                var usingPattern = @"using\s+([^\s;]+)";
                var usingMatches = Regex.Matches(content, usingPattern);
                foreach (Match match in usingMatches)
                {
                    imports.Add(new FileImport
                    {
                        ImportPath = match.Groups[1].Value,
                        ImportType = "Using",
                        Location = new Location
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        IsUsed = true
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
            _logger.LogError(ex, "Error analyzing file structure for {File}", filePath);
        }

        return null;
    }

    public async Task<FileComplexity?> AnalyzeFileComplexityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var linesOfCode = lines.Length;

            var methodPattern = @"(?:public|private|protected|internal)?\s*(?:static|async|virtual|override)?\s*[\w<>]+\s+(\w+)\s*\(";
            var methodMatches = Regex.Matches(content, methodPattern);

            var methodComplexities = new List<MethodComplexity>();
            foreach (Match match in methodMatches)
            {
                var methodName = match.Groups[1].Value;
                var lineNum = content.Substring(0, match.Index).Split('\n').Length;

                var methodContent = ExtractMethodContent(content, match.Index);
                var complexity = CalculateCyclomaticComplexity(methodContent);

                methodComplexities.Add(new MethodComplexity
                {
                    MethodName = methodName,
                    Location = new Location
                    {
                        FilePath = filePath,
                        Line = lineNum,
                        Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                    },
                    CyclomaticComplexity = complexity
                });
            }

            var totalComplexity = methodComplexities.Sum(m => m.CyclomaticComplexity);
            var cognitiveComplexity = totalComplexity * 1.5;

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
            _logger.LogError(ex, "Error analyzing file complexity for {File}", filePath);
        }

        return null;
    }

    public async Task<FileDependencies?> AnalyzeFileDependenciesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var dependencies = new List<string>();
            var externalDependencies = new List<string>();

            if (Path.GetExtension(filePath).ToLowerInvariant() == ".cs")
            {
                var usingPattern = @"using\s+([^\s;]+)";
                var matches = Regex.Matches(content, usingPattern);
                dependencies.AddRange(matches.Cast<Match>().Select(m => m.Groups[1].Value));

                externalDependencies = dependencies
                    .Where(d => d.StartsWith("System.") || d.StartsWith("Microsoft.") ||
                                d.Contains('.') && !d.StartsWith("Project"))
                    .ToList();
            }

            return new FileDependencies
            {
                FilePath = filePath,
                InternalDependencies = dependencies.Except(externalDependencies).Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "Using",
                    Locations = new List<Location> { new Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList(),
                ExternalDependencies = externalDependencies.Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "External",
                    Locations = new List<Location> { new Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file dependencies for {File}", filePath);
        }

        return null;
    }

    public async Task<FileQuality?> AnalyzeFileQualityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var complexity = await AnalyzeFileComplexityAsync(filePath, ct);
            var dependencies = await AnalyzeFileDependenciesAsync(filePath, ct);

            if (complexity == null || dependencies == null) return null;

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
            _logger.LogError(ex, "Error analyzing file quality for {File}", filePath);
        }

        return null;
    }

    public async Task<List<FileIssue>?> DetectFileIssuesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var issues = new List<FileIssue>();
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();

                // Check line length on the original line (including indentation)
                if (line.Length > 120)
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Style",
                        Severity = "Warning",
                        Message = "Line exceeds 120 characters",
                        Location = new Location { FilePath = filePath, Line = i + 1, Column = 120 }
                    });
                }

                if (trimmedLine.Contains("TODO") || trimmedLine.Contains("FIXME"))
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Documentation",
                        Severity = "Info",
                        Message = "Unresolved TODO or FIXME comment",
                        Location = new Location
                        {
                            FilePath = filePath,
                            Line = i + 1,
                            Column = line.IndexOf("TODO") >= 0 ? line.IndexOf("TODO") : line.IndexOf("FIXME")
                        }
                    });
                }
            }

            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting file issues for {File}", filePath);
        }

        return null;
    }

    public async Task<List<FileSuggestion>?> GenerateFileSuggestionsAsync(string filePath, CancellationToken ct)
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
                    Location = new Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            if (dependencies != null && (dependencies.InternalDependencies.Count + dependencies.ExternalDependencies.Count) > 20)
            {
                suggestions.Add(new FileSuggestion
                {
                    Type = "Dependency",
                    Description = "Consider reducing the number of dependencies",
                    Location = new Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating file suggestions for {File}", filePath);
        }

        return null;
    }

    private static string ExtractMethodContent(string content, int startIndex)
    {
        var braceCount = 0;
        var inMethod = false;
        var startIdx = startIndex;
        var endIdx = startIndex;

        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                if (!inMethod)
                {
                    inMethod = true;
                    startIdx = i;
                }
                braceCount++;
            }
            else if (content[i] == '}')
            {
                braceCount--;
                if (braceCount == 0 && inMethod)
                {
                    endIdx = i + 1;
                    break;
                }
            }
        }

        return content.Substring(startIdx, endIdx - startIdx);
    }

    private static int CalculateCyclomaticComplexity(string methodContent)
    {
        var complexity = 1;

        // Word-based keywords (use word boundary)
        var wordKeywords = new[] { "if", "else", "while", "for", "foreach", "switch", "case" };
        foreach (var keyword in wordKeywords)
        {
            complexity += Regex.Matches(methodContent, $@"\b{keyword}\b").Count;
        }

        // Operator-based keywords (no word boundary)
        var operatorKeywords = new[] { "&&", @"\|\|", @"\?:" };
        foreach (var keyword in operatorKeywords)
        {
            complexity += Regex.Matches(methodContent, keyword).Count;
        }

        return complexity;
    }
}
