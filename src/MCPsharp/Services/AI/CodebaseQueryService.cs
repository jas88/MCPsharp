using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI-powered codebase query service that processes verbose Roslyn data
/// and returns concise, human-readable answers.
/// This implements the AI-Powered MCP pattern to avoid context pollution.
/// </summary>
public partial class CodebaseQueryService
{
    private const int MaxRelevantFiles = 5;
    private const int MaxSnippetsPerFile = 5;
    private const int MaxContextChars = 12_000;
    private const int MinTermLength = 3;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "was", "were", "how", "what", "where", "which", "who", "why",
        "does", "did", "this", "that", "these", "those", "with", "from", "into", "about", "there",
        "their", "use", "used", "uses", "using", "can", "could", "should", "would", "will", "all",
        "any", "code", "file", "files", "class", "method", "methods", "find", "show", "list", "get"
    };

    private static readonly char[] PathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private static readonly string[] ExcludedDirectorySegments = { "bin", "obj", ".git", "node_modules" };

    private static readonly string[] ConfigKeywords = { "config", "configuration", "setting", "settings", "appsettings", "option", "options" };

    private readonly IAIProvider? _aiProvider;
    private readonly ProjectContextManager _projectContext;
    private readonly ILogger<CodebaseQueryService> _logger;

    public CodebaseQueryService(
        IAIProvider? aiProvider,
        ProjectContextManager projectContext,
        ILogger<CodebaseQueryService> logger)
    {
        _aiProvider = aiProvider;
        _projectContext = projectContext;
        _logger = logger;
    }

    public bool IsAvailable => _aiProvider != null;

    /// <summary>
    /// Name of the configured AI provider (e.g. "Ollama"), or null when no provider is available.
    /// </summary>
    public string? ProviderName => _aiProvider?.ProviderName;

    /// <summary>
    /// Ask a natural language question about the codebase.
    /// The AI processes verbose internal data and returns a concise answer.
    /// </summary>
    /// <param name="question">Natural language question</param>
    /// <param name="focusPath">Optional path to focus the search</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Concise answer with file:line references where relevant</returns>
    public async Task<string> AskCodebaseAsync(
        string question,
        string? focusPath = null,
        CancellationToken cancellationToken = default)
    {
        if (_aiProvider == null)
        {
            throw new InvalidOperationException(
                "AI provider not available. Install Ollama or configure OpenRouter API key.");
        }

        LogProcessingQuery(_logger, question);

        // Build internal context (this can be 100KB+ of data, but stays internal)
        var context = await BuildContextAsync(question, focusPath, cancellationToken);

        // Construct prompt for AI
        var prompt = BuildPrompt(question, context);

        // AI processes and returns concise answer
        var startTime = DateTime.UtcNow;
        var answer = await _aiProvider.ProcessQueryAsync(prompt, cancellationToken);
        var elapsed = DateTime.UtcNow - startTime;

        LogQueryCompleted(_logger, elapsed.TotalSeconds, _aiProvider.ProviderName, _aiProvider.ModelName);

        return answer;
    }

    /// <summary>
    /// Build internal context for the question.
    /// This method queries the file system to gather relevant information using keyword relevance ranking.
    /// The verbose data never reaches the main Claude Code agent.
    /// </summary>
    private async Task<CodebaseContext> BuildContextAsync(
        string question,
        string? focusPath,
        CancellationToken cancellationToken)
    {
        var projectInfo = _projectContext.GetProjectInfo();

        var context = new CodebaseContext
        {
            ProjectRoot = projectInfo?["rootPath"]?.ToString() ?? "",
            FileCount = projectInfo != null && projectInfo.TryGetValue("fileCount", out var fileCount)
                ? Convert.ToInt32(fileCount, CultureInfo.InvariantCulture)
                : 0
        };

        var searchRoot = ResolveSearchRoot(context.ProjectRoot, focusPath);
        if (searchRoot == null)
        {
            return context;
        }

        PopulateProjectMetadata(context, searchRoot, context.ProjectRoot);

        var terms = ExtractSearchTerms(question);
        if (terms.Count > 0)
        {
            context.RelevantSnippets.AddRange(
                await FindRelevantSnippetsAsync(searchRoot, context.ProjectRoot, terms, cancellationToken));
        }

        if (ConfigKeywords.Any(k => question.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            context.ConfigurationFiles.AddRange(FindConfigurationFiles(searchRoot, context.ProjectRoot));
        }

        return context;
    }

    /// <summary>
    /// Resolve the directory to search: the focus path (if it exists) or the project root.
    /// </summary>
    private static string? ResolveSearchRoot(string projectRoot, string? focusPath)
    {
        if (!string.IsNullOrWhiteSpace(focusPath))
        {
            var candidate = Path.IsPathRooted(focusPath) || string.IsNullOrEmpty(projectRoot)
                ? focusPath
                : Path.Combine(projectRoot, focusPath);

            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return Directory.Exists(projectRoot) ? projectRoot : null;
    }

    /// <summary>
    /// Fill in project name and target framework from the nearest project file.
    /// </summary>
    private static void PopulateProjectMetadata(CodebaseContext context, string searchRoot, string projectRoot)
    {
        var baseDir = Directory.Exists(searchRoot) ? searchRoot : Path.GetDirectoryName(searchRoot) ?? projectRoot;
        var projectFile = EnumerateFilesSafe(baseDir, "*.csproj").FirstOrDefault()
            ?? (Directory.Exists(projectRoot) ? EnumerateFilesSafe(projectRoot, "*.csproj").FirstOrDefault() : null);

        if (projectFile == null)
        {
            return;
        }

        context.ProjectName = Path.GetFileNameWithoutExtension(projectFile);
        try
        {
            var xml = System.Xml.Linq.XDocument.Load(projectFile);
            context.TargetFramework = xml.Descendants()
                .FirstOrDefault(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")?.Value ?? "";
        }
        catch (System.Xml.XmlException)
        {
            // Malformed project file: leave the target framework unknown
            context.TargetFramework = "";
        }
    }

    /// <summary>
    /// Split the question into distinct identifier-like search terms, ignoring stop words.
    /// </summary>
    private static List<string> ExtractSearchTerms(string question)
    {
        var terms = new List<string>();
        var current = new StringBuilder();

        foreach (var c in question.Append(' '))
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                current.Append(c);
                continue;
            }

            var term = current.ToString();
            current.Clear();
            if (term.Length >= MinTermLength
                && !StopWords.Contains(term)
                && !terms.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                terms.Add(term);
            }
        }

        return terms;
    }

    /// <summary>
    /// Rank C# files by keyword relevance and extract matching lines (with line numbers) from the best matches.
    /// </summary>
    private static async Task<List<CodeSnippet>> FindRelevantSnippetsAsync(
        string searchRoot,
        string projectRoot,
        List<string> terms,
        CancellationToken cancellationToken)
    {
        var files = File.Exists(searchRoot) ? new[] { searchRoot } : EnumerateFilesSafe(searchRoot, "*.cs");
        var scored = new List<(string Path, int Score, string[] Lines)>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, cancellationToken);
            }
            catch (IOException)
            {
                continue; // File locked or removed during the scan
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            var score = terms.Sum(t =>
                (fileName.Contains(t, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
                + lines.Count(l => l.Contains(t, StringComparison.OrdinalIgnoreCase)));

            if (score > 0)
            {
                scored.Add((file, score, lines));
            }
        }

        var snippets = new List<CodeSnippet>();
        var totalChars = 0;

        foreach (var (path, _, lines) in scored.OrderByDescending(s => s.Score).Take(MaxRelevantFiles))
        {
            var relativePath = string.IsNullOrEmpty(projectRoot) ? path : Path.GetRelativePath(projectRoot, path);
            var matches = lines
                .Select((text, index) => (Text: text.Trim(), LineNumber: index + 1))
                .Where(l => terms.Any(t => l.Text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxSnippetsPerFile);

            foreach (var (text, lineNumber) in matches)
            {
                if (totalChars + text.Length > MaxContextChars)
                {
                    return snippets;
                }

                snippets.Add(new CodeSnippet(relativePath, lineNumber, text));
                totalChars += text.Length;
            }
        }

        return snippets;
    }

    /// <summary>
    /// Find configuration files (appsettings, json/yaml config) under the search root.
    /// </summary>
    private static List<string> FindConfigurationFiles(string searchRoot, string projectRoot)
    {
        if (!Directory.Exists(searchRoot))
        {
            return new List<string>();
        }

        return EnumerateFilesSafe(searchRoot, "*.*")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => string.IsNullOrEmpty(projectRoot) ? f : Path.GetRelativePath(projectRoot, f))
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Enumerate files recursively, skipping build output and VCS directories and inaccessible folders.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, pattern, options)
            .Where(f => !Path.GetRelativePath(root, f).Split(PathSeparators)
                .Any(segment => ExcludedDirectorySegments.Contains(segment, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Build the prompt for the AI model.
    /// This includes the context and question, formatted for optimal results.
    /// </summary>
    private static string BuildPrompt(string question, CodebaseContext context)
    {
        var relevantCode = new StringBuilder();
        foreach (var snippet in context.RelevantSnippets)
        {
            relevantCode.Append(CultureInfo.InvariantCulture, $"{snippet.FilePath}:{snippet.LineNumber}: {snippet.Text}").AppendLine();
        }

        var configFiles = context.ConfigurationFiles.Count > 0
            ? string.Join(Environment.NewLine, context.ConfigurationFiles.Select(f => $"- {f}"))
            : "(none)";

        return $@"You are a C# codebase expert helping analyze a .NET project.

## Project Context

Project: {context.ProjectName}
Target Framework: {context.TargetFramework}
Files: {context.FileCount}
Root: {context.ProjectRoot}

## Relevant Code (file:line: text)

{(relevantCode.Length > 0 ? relevantCode.ToString() : "(no matching code found)")}

## Configuration Files

{configFiles}

## Instructions

Answer the following question concisely and accurately:
- Include file paths and line numbers where relevant (format: file.cs:123)
- Focus on the most important information
- Use bullet points for lists
- Keep answers under 10 lines unless more detail is needed

## Question

{question}

## Answer

";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing codebase query: {Question}")]
    private static partial void LogProcessingQuery(ILogger logger, string question);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI query completed in {ElapsedSeconds}s using {Provider}/{Model}")]
    private static partial void LogQueryCompleted(ILogger logger, double elapsedSeconds, string provider, string model);

    private sealed record CodeSnippet(string FilePath, int LineNumber, string Text);

    private sealed class CodebaseContext
    {
        public string ProjectRoot { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public int FileCount { get; set; }
        public List<CodeSnippet> RelevantSnippets { get; } = new();
        public List<string> ConfigurationFiles { get; } = new();
    }
}
