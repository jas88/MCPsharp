using System.Text;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class UniversalFileOperations
{
    #region Utility Methods

    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        return Path.GetFullPath(Path.Combine(_rootPath, path));
    }

    private bool ValidatePath(string fullPath) => PathContainment.IsWithin(_rootPath, fullPath);

    private static bool IsHidden(FileSystemInfo info)
    {
        return info.Attributes.HasFlag(FileAttributes.Hidden) ||
               info.Name.StartsWith('.');
    }

    private static string DetectLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".csx" => "csharp",
            ".vb" => "vb",
            ".fs" => "fsharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "jsx",
            ".tsx" => "tsx",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            ".sql" => "sql",
            ".html" => "html",
            ".css" => "css",
            ".scss" or ".sass" => "scss",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".cpp" or ".cxx" or ".cc" => "cpp",
            ".c" => "c",
            ".h" or ".hpp" => "cpp",
            ".sh" or ".bash" => "shell",
            ".ps1" => "powershell",
            _ => "text"
        };
    }

    private static FileCategory DetermineFileCategory(string extension, string relativePath)
    {
        // Check by extension first
        var ext = extension.ToLowerInvariant();

        if (CodeFileExtensions.Contains(ext))
            return FileCategory.SourceCode;
        if (ConfigFileExtensions.Contains(ext))
            return FileCategory.Configuration;
        if (DocumentationExtensions.Contains(ext))
            return FileCategory.Documentation;
        if (TestFileExtensions.Contains(ext))
            return FileCategory.Test;
        if (BuildFileExtensions.Contains(ext))
            return FileCategory.Build;

        // Check by path pattern
        var pathLower = relativePath.ToLowerInvariant();
        if (pathLower.Contains("test") || pathLower.Contains("spec"))
            return FileCategory.Test;
        if (pathLower.Contains("doc") || pathLower.Contains("readme"))
            return FileCategory.Documentation;

        return FileCategory.Other;
    }

    private static readonly HashSet<string> CodeFileExtensions = new()
    {
        ".cs", ".csx", ".vb", ".fs", ".js", ".ts", ".jsx", ".tsx",
        ".py", ".go", ".rs", ".java", ".cpp", ".cxx", ".cc", ".c",
        ".h", ".hpp", ".swift", ".kt", ".scala", ".rb", ".php"
    };

    private static readonly HashSet<string> ConfigFileExtensions = new()
    {
        ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg",
        ".config", ".props", ".targets", ".csproj", ".sln"
    };

    private static readonly HashSet<string> DocumentationExtensions = new()
    {
        ".md", ".txt", ".rst", ".adoc", ".pdf", ".doc", ".docx"
    };

    private static readonly HashSet<string> TestFileExtensions = new()
    {
        ".test.cs", ".tests.cs", ".spec.cs", ".test.js", ".spec.js",
        ".test.ts", ".spec.ts", ".test.py", "test_*.py"
    };

    private static readonly HashSet<string> BuildFileExtensions = new()
    {
        ".props", ".targets", ".proj", ".build", ".cake", "Makefile",
        "CMakeLists.txt", "Dockerfile", "docker-compose.yml"
    };

    private static async Task<string> CalculateFileHashAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static async Task<long> CalculateDirectorySizeAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(file => new System.IO.FileInfo(file).Length);
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }, ct);
    }

    private static string? GenerateContentPreview(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength] + "...";
    }

    private static string ApplyContentTransformations(string content, List<ContentTransformation> transformations)
    {
        var result = content;

        foreach (var transformation in transformations)
        {
            result = transformation.Type switch
            {
                TransformationType.Trim => result.Trim(),
                TransformationType.NormalizeLineEndings => result.Replace("\r\n", "\n").Replace('\r', '\n'),
                TransformationType.RemoveBlankLines => string.Join('\n', result.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))),
                TransformationType.SortLines => string.Join('\n', result.Split('\n').OrderBy(line => line)),
                TransformationType.RemoveComments => RemoveComments(result),
                _ => result
            };
        }

        return result;
    }

    private static string RemoveComments(string content)
    {
        // Simple comment removal - could be enhanced per language
        var lines = content.Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith('#') && !trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    private static async Task<List<string>> ExtractFileDependenciesAsync(string fullPath, CancellationToken ct)
    {
        var dependencies = new List<string>();

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            // Simple dependency extraction - could be enhanced per language
            if (extension == ".cs")
            {
                // Extract using statements
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    content,
                    @"using\s+([\w.]+);",
                    System.Text.RegularExpressions.RegexOptions.Multiline, TimeSpan.FromSeconds(1));

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    dependencies.Add($"namespace:{match.Groups[1].Value}");
                }
            }
        }
        catch (IOException)
        {
            // Ignore unreadable files in dependency extraction
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore inaccessible files in dependency extraction
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // Ignore pathological content in dependency extraction
        }

        return dependencies;
    }

    private static bool IsValidUtf8(string content)
    {
        // Encoding.UTF8 uses replacement fallback, so encoding/decoding never throws;
        // lone surrogates are replaced and therefore fail the round-trip comparison.
        var bytes = Encoding.UTF8.GetBytes(content);
        var decoded = Encoding.UTF8.GetString(bytes);
        return decoded == content;
    }

    private async Task<string?> CreateTransactionBackupAsync(List<FileOperationDefinition> operations, CancellationToken ct)
    {
        var backupDir = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", Guid.NewGuid().ToString());
        Directory.CreateDirectory(backupDir);

        try
        {
            foreach (var operationPath in operations.Select(o => o.Path).Where(p => File.Exists(GetFullPath(p))))
            {
                var sourcePath = GetFullPath(operationPath);
                var backupPath = Path.Combine(backupDir, operationPath.Replace('/', '_').Replace('\\', '_'));
                await Task.Run(() => File.Copy(sourcePath, backupPath), ct);
            }

            return backupDir;
        }
        catch (Exception ex)
        {
            LogIgnoredException(_logger, ex, "creating transaction backup", backupDir);
            // Cleanup on failure
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
            return null;
        }
    }

    private async Task RollbackTransactionAsync(string backupPath, CancellationToken ct)
    {
        if (!Directory.Exists(backupPath))
            return;

        foreach (var backupFile in Directory.GetFiles(backupPath))
        {
            try
            {
                var fileName = Path.GetFileName(backupFile);
                var originalPath = fileName.Replace('_', Path.DirectorySeparatorChar);
                var targetPath = GetFullPath(originalPath);

                await Task.Run(() => File.Copy(backupFile, targetPath, true), ct);
            }
            catch (Exception ex)
            {
                // Continue with other files
                LogIgnoredException(_logger, ex, "restoring backup file", backupFile);
            }
        }
    }

    private static async Task CleanupTransactionBackupAsync(string backupPath, CancellationToken ct)
    {
        try
        {
            await Task.Run(() => Directory.Delete(backupPath, true), ct);
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    #endregion
}
