using System.Collections.Immutable;
using System.Text.RegularExpressions;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers.BuiltIn;

/// <summary>
/// Built-in analyzer for upgrading NUnit from v2/v3 to v4 syntax
/// </summary>
public class NUnitUpgraderAnalyzer : IAnalyzer
{
    private static readonly ImmutableArray<string> _supportedExtensions = ImmutableArray.Create(".cs");
    private static readonly ImmutableArray<AnalyzerRule> Rules = ImmutableArray.Create(
        new AnalyzerRule
        {
            Id = "NUNIT001",
            Title = "Replace [TestFixture] attribute with [Test]",
            Description = "In NUnit v4, [TestFixture] attribute is no longer needed on test classes",
            Category = RuleCategory.Migration,
            DefaultSeverity = IssueSeverity.Info,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "attribute", "migration")
        },
        new AnalyzerRule
        {
            Id = "NUNIT002",
            Title = "Update Assert.AreEqual syntax",
            Description = "In NUnit v4, Assert.AreEqual should use consistent parameter order: expected, actual",
            Category = RuleCategory.Migration,
            DefaultSeverity = IssueSeverity.Warning,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "assert", "migration")
        },
        new AnalyzerRule
        {
            Id = "NUNIT003",
            Title = "Replace [SetUp] with [SetUp] attribute",
            Description = "In NUnit v4, [SetUp] attribute should be updated to [SetUp]",
            Category = RuleCategory.Migration,
            DefaultSeverity = IssueSeverity.Info,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "attribute", "migration")
        },
        new AnalyzerRule
        {
            Id = "NUNIT004",
            Title = "Update ExpectedException attribute",
            Description = "In NUnit v4, [ExpectedException] should be replaced with Assert.Throws",
            Category = RuleCategory.Migration,
            DefaultSeverity = IssueSeverity.Warning,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "exception", "migration")
        },
        new AnalyzerRule
        {
            Id = "NUNIT005",
            Title = "Replace string.IsNullOrEmpty with Assert.That",
            Description = "In NUnit v4, use Assert.That for more readable assertions",
            Category = RuleCategory.Style,
            DefaultSeverity = IssueSeverity.Info,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "assert", "style")
        },
        new AnalyzerRule
        {
            Id = "NUNIT006",
            Title = "Update using statements for NUnit v4",
            Description = "Update NUnit using statements to use NUnit.Framework v4 namespaces",
            Category = RuleCategory.Migration,
            DefaultSeverity = IssueSeverity.Info,
            IsEnabledByDefault = true,
            Tags = ImmutableArray.Create("nunit", "v4", "using", "migration")
        }
    );

    private readonly Dictionary<string, Regex> _patterns;
    private readonly Dictionary<string, Func<Match, string, AnalyzerFix[]>> _fixGenerators;

    public string Id => "NUnitUpgrader";
    public string Name => "NUnit v4 Migration Analyzer";
    public string Description => "Analyzes C# code for NUnit v2/v3 patterns and suggests v4 upgrades";
    public Version Version => new(1, 0, 0, 0);
    public string Author => "MCPsharp";
    public ImmutableArray<string> SupportedExtensions => _supportedExtensions;
    public bool IsEnabled { get; set; } = true;
    public AnalyzerConfiguration Configuration { get; set; } = new();

    public bool CanAnalyze(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))
            return false;

        // Check if the target path is a file with supported extension
        if (File.Exists(targetPath))
        {
            var extension = Path.GetExtension(targetPath);
            return _supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        // Check if the target path is a directory containing C# files
        if (Directory.Exists(targetPath))
        {
            return Directory.GetFiles(targetPath, "*.cs", SearchOption.AllDirectories)
                .Any(path => _supportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        }

        return false;
    }

    public NUnitUpgraderAnalyzer()
    {
        _patterns = new Dictionary<string, Regex>
        {
            ["NUNIT001"] = new Regex(@"\[(?:NUnit\.)?TestFixture\]", RegexOptions.Compiled),
            ["NUNIT002"] = new Regex(@"Assert\.AreEqual\(([^,]+),\s*([^,]+)\)", RegexOptions.Compiled),
            ["NUNIT003"] = new Regex(@"\[(?:NUnit\.)?SetUp\]", RegexOptions.Compiled),
            ["NUNIT004"] = new Regex(@"\[(?:NUnit\.)?ExpectedException\((?:typeof\(([^)]+)\)|([^)]+))\)\]", RegexOptions.Compiled),
            ["NUNIT005"] = new Regex(@"Assert\.(True|False)\(string\.IsNullOrEmpty\(([^)]+)\)\)", RegexOptions.Compiled),
            ["NUNIT006"] = new Regex(@"using\s+NUnit\.Framework(?:\.Tests)?;", RegexOptions.Compiled)
        };

        _fixGenerators = new Dictionary<string, Func<Match, string, AnalyzerFix[]>>
        {
            ["NUNIT001"] = GenerateTestFixtureFix,
            ["NUNIT002"] = GenerateAssertEqualsFix,
            ["NUNIT003"] = GenerateSetUpFix,
            ["NUNIT004"] = GenerateExpectedExceptionFix,
            ["NUNIT005"] = GenerateStringNullOrEmptyFix,
            ["NUNIT006"] = GenerateUsingFix
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // No initialization needed for this analyzer
    }

    public async Task<AnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var issues = new List<AnalyzerIssue>();
            var lines = content.Split('\n');

            foreach (var rule in Rules)
            {
                if (!IsRuleEnabled(rule.Id))
                    continue;

                var pattern = _patterns[rule.Id];
                var matches = pattern.Matches(content);

                foreach (Match match in matches)
                {
                    var (lineNumber, columnNumber) = GetPosition(content, match.Index);
                    var (endLineNumber, endColumnNumber) = GetPosition(content, match.Index + match.Length);

                    var issue = new AnalyzerIssue
                    {
                        RuleId = rule.Id,
                        AnalyzerId = Id,
                        Title = rule.Title,
                        Description = rule.Description,
                        FilePath = filePath,
                        LineNumber = lineNumber + 1,
                        ColumnNumber = columnNumber + 1,
                        EndLineNumber = endLineNumber + 1,
                        EndColumnNumber = endColumnNumber + 1,
                        Severity = rule.DefaultSeverity,
                        Confidence = Confidence.High,
                        Category = rule.Category,
                        HelpLink = $"https://docs.nunit.org/articles/nunit/writing-tests.html#{rule.Id.ToLower()}",
                        Properties = new Dictionary<string, object>
                        {
                            ["MatchText"] = match.Value,
                            ["Suggestion"] = GetSuggestion(rule.Id, match)
                        }.ToImmutableDictionary()
                    };

                    issues.Add(issue);
                }
            }

            return new AnalysisResult
            {
                FilePath = filePath,
                AnalyzerId = Id,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Success = true,
                Issues = issues.ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["IssuesFound"] = issues.Count,
                    ["RulesApplied"] = Rules.Count(r => IsRuleEnabled(r.Id))
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            return new AnalysisResult
            {
                FilePath = filePath,
                AnalyzerId = Id,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message,
                Issues = ImmutableArray<AnalyzerIssue>.Empty
            };
        }
    }

    public ImmutableArray<AnalyzerRule> GetRules() => Rules;

    public ImmutableArray<AnalyzerFix> GetFixes(string ruleId)
    {
        if (!_patterns.TryGetValue(ruleId, out var pattern) || !_fixGenerators.TryGetValue(ruleId, out var generator))
        {
            return ImmutableArray<AnalyzerFix>.Empty;
        }

        // Return a template fix that will be populated with actual data during fix generation
        return ImmutableArray.Create(new AnalyzerFix
        {
            Id = $"{ruleId}_FIX",
            RuleId = ruleId,
            Title = $"Apply {ruleId} fix",
            Description = $"Apply the recommended fix for {Rules.First(r => r.Id == ruleId).Title}",
            Confidence = Confidence.High,
            IsInteractive = false,
            IsBatchable = true
        });
    }

    public AnalyzerCapabilities GetCapabilities()
    {
        return new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#" },
            SupportedFileTypes = new[] { ".cs" },
            MaxFileSize = 10 * 1024 * 1024, // 10MB
            CanAnalyzeProjects = true,
            CanAnalyzeSolutions = true,
            SupportsParallelProcessing = false,
            CanFixIssues = true
        };
    }

    public async Task DisposeAsync()
    {
        // No disposal needed
    }

    private bool IsRuleEnabled(string ruleId)
    {
        return Configuration.Rules.TryGetValue(ruleId, out var ruleConfig) ? ruleConfig.IsEnabled : true;
    }

    private (int line, int column) GetPosition(string content, int index)
    {
        var linesBefore = content.Substring(0, index).Split('\n');
        var line = linesBefore.Length - 1;
        var column = linesBefore[^1].Length;
        return (line, column);
    }

    private string GetSuggestion(string ruleId, Match match)
    {
        return ruleId switch
        {
            "NUNIT001" => "Remove [TestFixture] attribute - not needed in NUnit v4",
            "NUNIT002" => "Reorder parameters to Assert.AreEqual(expected, actual)",
            "NUNIT003" => "[SetUp] attribute is already correct for NUnit v4",
            "NUNIT004" => "Replace with Assert.Throws<ExceptionType>(() => method())",
            "NUNIT005" => "Use Assert.That(actual, Is.Null.Or.Empty) for better readability",
            "NUNIT006" => "Update to using NUnit.Framework; (remove .Tests if present)",
            _ => "See NUnit v4 documentation for migration guidance"
        };
    }

    private AnalyzerFix[] GenerateTestFixtureFix(Match match, string content)
    {
        var fix = new AnalyzerFix
        {
            Id = $"NUNIT001_FIX_{match.Index}",
            RuleId = "NUNIT001",
            Title = "Remove [TestFixture] attribute",
            Description = "Remove the [TestFixture] attribute as it's not needed in NUnit v4",
            Confidence = Confidence.High,
            IsInteractive = false,
            IsBatchable = true,
            Edits = ImmutableArray.Create<TextEdit>(new ReplaceEdit
            {
                StartLine = GetPosition(content, match.Index).line + 1,
                StartColumn = GetPosition(content, match.Index).column + 1,
                EndLine = GetPosition(content, match.Index + match.Length).line + 1,
                EndColumn = GetPosition(content, match.Index + match.Length).column + 1,
                NewText = string.Empty
            })
        };

        return new[] { fix };
    }

    private AnalyzerFix[] GenerateAssertEqualsFix(Match match, string content)
    {
        var actual = match.Groups[2].Value.Trim();
        var expected = match.Groups[1].Value.Trim();

        var fix = new AnalyzerFix
        {
            Id = $"NUNIT002_FIX_{match.Index}",
            RuleId = "NUNIT002",
            Title = "Reorder Assert.AreEqual parameters",
            Description = "Change Assert.AreEqual(actual, expected) to Assert.AreEqual(expected, actual)",
            Confidence = Confidence.High,
            IsInteractive = false,
            IsBatchable = true,
            Edits = ImmutableArray.Create<TextEdit>(new ReplaceEdit
            {
                StartLine = GetPosition(content, match.Index).line + 1,
                StartColumn = GetPosition(content, match.Index).column + 1,
                EndLine = GetPosition(content, match.Index + match.Length).line + 1,
                EndColumn = GetPosition(content, match.Index + match.Length).column + 1,
                NewText = $"Assert.AreEqual({expected}, {actual})"
            })
        };

        return new[] { fix };
    }

    private AnalyzerFix[] GenerateSetUpFix(Match match, string content)
    {
        // [SetUp] is already correct in v4, so this is just informational
        return Array.Empty<AnalyzerFix>();
    }

    private AnalyzerFix[] GenerateExpectedExceptionFix(Match match, string content)
    {
        var exceptionType = match.Groups[1].Value;
        if (string.IsNullOrEmpty(exceptionType))
            exceptionType = match.Groups[2].Value;

        var fix = new AnalyzerFix
        {
            Id = $"NUNIT004_FIX_{match.Index}",
            RuleId = "NUNIT004",
            Title = "Replace ExpectedException with Assert.Throws",
            Description = "Replace [ExpectedException] attribute with Assert.Throws in test method",
            Confidence = Confidence.Medium,
            IsInteractive = true,
            IsBatchable = false,
            RequiredInputs = ImmutableArray.Create("TestMethodName"),
            Edits = ImmutableArray.Create<TextEdit>(new ReplaceEdit
            {
                StartLine = GetPosition(content, match.Index).line + 1,
                StartColumn = GetPosition(content, match.Index).column + 1,
                EndLine = GetPosition(content, match.Index + match.Length).line + 1,
                EndColumn = GetPosition(content, match.Index + match.Length).column + 1,
                NewText = $"// Replace [ExpectedException] with Assert.Throws<{exceptionType}> in test method"
            })
        };

        return new[] { fix };
    }

    private AnalyzerFix[] GenerateStringNullOrEmptyFix(Match match, string content)
    {
        var isTrue = match.Groups[1].Value == "True";
        var parameter = match.Groups[2].Value.Trim();

        var assertion = isTrue ? "Is.Null.Or.Empty" : "Is.Not.Null.And.Not.Empty";

        var fix = new AnalyzerFix
        {
            Id = $"NUNIT005_FIX_{match.Index}",
            RuleId = "NUNIT005",
            Title = "Replace string.IsNullOrEmpty with Assert.That",
            Description = "Use more readable Assert.That syntax",
            Confidence = Confidence.High,
            IsInteractive = false,
            IsBatchable = true,
            Edits = ImmutableArray.Create<TextEdit>(new ReplaceEdit
            {
                StartLine = GetPosition(content, match.Index).line + 1,
                StartColumn = GetPosition(content, match.Index).column + 1,
                EndLine = GetPosition(content, match.Index + match.Length).line + 1,
                EndColumn = GetPosition(content, match.Index + match.Length).column + 1,
                NewText = $"Assert.That({parameter}, {assertion})"
            })
        };

        return new[] { fix };
    }

    private AnalyzerFix[] GenerateUsingFix(Match match, string content)
    {
        var fix = new AnalyzerFix
        {
            Id = $"NUNIT006_FIX_{match.Index}",
            RuleId = "NUNIT006",
            Title = "Update NUnit using statement",
            Description = "Update to standard NUnit.Framework using statement",
            Confidence = Confidence.High,
            IsInteractive = false,
            IsBatchable = true,
            Edits = ImmutableArray.Create<TextEdit>(new ReplaceEdit
            {
                StartLine = GetPosition(content, match.Index).line + 1,
                StartColumn = GetPosition(content, match.Index).column + 1,
                EndLine = GetPosition(content, match.Index + match.Length).line + 1,
                EndColumn = GetPosition(content, match.Index + match.Length).column + 1,
                NewText = "using NUnit.Framework;"
            })
        };

        return new[] { fix };
    }
}