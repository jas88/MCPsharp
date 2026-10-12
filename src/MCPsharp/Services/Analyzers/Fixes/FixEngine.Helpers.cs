using System.Collections.Immutable;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers.Fixes;

public partial class FixEngine
{
    private static FixPreview GeneratePreview(
        AnalyzerFix fix,
        ImmutableArray<AnalyzerIssue> issues,
        Dictionary<string, string> fileContents)
    {
        var issue = issues.FirstOrDefault(i => fix.RuleId == i.RuleId);
        var filePath = fix.Edits.FirstOrDefault()?.FilePath ?? issue?.FilePath ?? string.Empty;

        if (!fileContents.TryGetValue(filePath, out var originalContent))
        {
            throw new InvalidOperationException($"File content not available for preview: {filePath}");
        }

        var modifiedContent = originalContent;

        // Apply edits to generate preview
        foreach (var edit in fix.Edits.OrderByDescending(e => e.StartLine))
        {
            modifiedContent = ApplyTextEdit(modifiedContent, edit);
        }

        return new FixPreview
        {
            FixId = fix.Id,
            IssueId = issue?.Id ?? string.Empty,
            FilePath = filePath,
            OriginalContent = originalContent,
            ModifiedContent = modifiedContent,
            Edits = fix.Edits,
            Description = fix.Description,
            Confidence = fix.Confidence
        };
    }

    private async Task<Dictionary<string, string>> CreateBackupsAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken)
    {
        var backupPaths = new Dictionary<string, string>();

        foreach (var filePath in filePaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var backupPath = Path.Combine(_backupDirectory, $"{Guid.NewGuid()}{Path.GetExtension(filePath)}");
                    await using var sourceStream = File.OpenRead(filePath);
                    await using var destinationStream = File.Create(backupPath);
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                    backupPaths[filePath] = backupPath;
                }
            }
            catch (Exception ex)
            {
                LogErrorCreatingBackup(_logger, ex, filePath);
            }
        }

        return backupPaths;
    }

    private async Task<FixResult> ApplySingleFixAsync(
        AnalyzerFix fix,
        CancellationToken cancellationToken)
    {
        var modifiedFiles = new List<string>();

        foreach (var edit in fix.Edits)
        {
            try
            {
                var editFilePath = edit.FilePath ?? string.Empty;
                var edits = new[] { new ReplaceEdit
                {
                    StartLine = edit.StartLine,
                    StartColumn = edit.StartColumn,
                    EndLine = edit.EndLine,
                    EndColumn = edit.EndColumn,
                    NewText = edit.NewText
                }};
                var result = await _fileOperations.EditFileAsync(editFilePath, edits, cancellationToken);

                if (result.Success)
                {
                    modifiedFiles.Add(editFilePath);
                }
                else
                {
                    return new FixResult
                    {
                        Success = false,
                        FixId = fix.Id,
                        AnalyzerId = fix.AnalyzerId,
                        ErrorMessage = result.Error,
                        ModifiedFiles = modifiedFiles.ToImmutableArray()
                    };
                }
            }
            catch (Exception ex)
            {
                LogErrorApplyingEdit(_logger, ex, fix.Id, edit.FilePath);
                return new FixResult
                {
                    Success = false,
                    FixId = fix.Id,
                    AnalyzerId = fix.AnalyzerId,
                    ErrorMessage = ex.Message,
                    ModifiedFiles = modifiedFiles.ToImmutableArray()
                };
            }
        }

        return new FixResult
        {
            Success = true,
            FixId = fix.Id,
            AnalyzerId = fix.AnalyzerId,
            ModifiedFiles = modifiedFiles.ToImmutableArray(),
            AppliedAt = DateTime.UtcNow
        };
    }

    private static string ApplyTextEdit(string content, TextEdit edit)
    {
        var lines = content.Split('\n');

        if (edit.StartLine >= lines.Length || edit.EndLine >= lines.Length)
            return content;

        var startLine = edit.StartLine;
        var endLine = edit.EndLine;

        if (startLine == endLine)
        {
            var line = lines[startLine];
            lines[startLine] = string.Concat(
                line.AsSpan(0, edit.StartColumn),
                edit.NewText,
                line.AsSpan(Math.Min(edit.EndColumn, line.Length)));
        }
        else
        {
            lines[startLine] = string.Concat(lines[startLine].AsSpan(0, edit.StartColumn), edit.NewText);
            var newLines = new List<string>(lines.Take(startLine + 1));
            if (endLine + 1 < lines.Length)
            {
                newLines.Add(lines[endLine].Substring(Math.Min(edit.EndColumn, lines[endLine].Length)));
                newLines.AddRange(lines.Skip(endLine + 1));
            }
            lines = newLines.ToArray();
        }

        return string.Join('\n', lines);
    }

    private static List<ValidationError> ValidateCSharpSyntax(string content)
    {
        var errors = new List<ValidationError>();

        // Basic syntax validation - check for common issues
        if (content.Count(c => c == '{') != content.Count(c => c == '}'))
        {
            errors.Add(new ValidationError
            {
                FilePath = "unknown",
                Description = "Mismatched braces detected",
                FixId = "Validation",
                Severity = Models.ValidationSeverity.Error
            });
        }

        if (content.Count(c => c == '(') != content.Count(c => c == ')'))
        {
            errors.Add(new ValidationError
            {
                FilePath = "unknown",
                Description = "Mismatched parentheses detected",
                FixId = "Validation",
                Severity = Models.ValidationSeverity.Error
            });
        }

        return errors;
    }

    /// <summary>
    /// Converts an AnalyzerFix to a FixPreview
    /// </summary>
    private static FixPreview ConvertToFixPreview(AnalyzerFix fix)
    {
        return new FixPreview
        {
            FixId = fix.Id,
            IssueId = fix.RuleId, // Use RuleId as IssueId for now
            FilePath = fix.Edits.FirstOrDefault()?.FilePath ?? string.Empty,
            OriginalContent = string.Empty, // Not available in AnalyzerFix
            ModifiedContent = string.Empty, // Not available in AnalyzerFix
            Edits = fix.Edits,
            Description = fix.Description,
            Confidence = fix.Confidence
        };
    }
}
