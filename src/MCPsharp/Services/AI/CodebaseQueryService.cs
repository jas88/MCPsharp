using Microsoft.Extensions.Logging;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.AI;

/// <summary>
/// AI-powered codebase query service that processes verbose Roslyn data
/// and returns concise, human-readable answers.
/// This implements the AI-Powered MCP pattern to avoid context pollution.
/// </summary>
public class CodebaseQueryService
{
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

        _logger.LogInformation("Processing codebase query: {Question}", question);

        // Build internal context (this can be 100KB+ of data, but stays internal)
        var context = await BuildContextAsync(question, focusPath, cancellationToken);

        // Construct prompt for AI
        var prompt = BuildPrompt(question, context);

        // AI processes and returns concise answer
        var startTime = DateTime.UtcNow;
        var answer = await _aiProvider.ProcessQueryAsync(prompt, cancellationToken);
        var elapsed = DateTime.UtcNow - startTime;

        _logger.LogInformation(
            "AI query completed in {ElapsedSeconds}s using {Provider}/{Model}",
            elapsed.TotalSeconds,
            _aiProvider.ProviderName,
            _aiProvider.ModelName);

        return answer;
    }

    /// <summary>
    /// Build internal context for the question.
    /// This method queries Roslyn, file system, etc. to gather relevant information.
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
            Question = question,
            FileCount = projectInfo != null && projectInfo.ContainsKey("fileCount")
                ? Convert.ToInt32(projectInfo["fileCount"])
                : 0
        };

        // TODO: Implement semantic search / relevance ranking
        // TODO: Add relevant code snippets based on semantic search
        // TODO: Add relevant type/method signatures
        // TODO: Add configuration files if relevant to question

        return await Task.FromResult(context);
    }

    /// <summary>
    /// Build the prompt for the AI model.
    /// This includes the context and question, formatted for optimal results.
    /// </summary>
    private static string BuildPrompt(string question, CodebaseContext context)
    {
        return $@"You are a C# codebase expert helping analyze a .NET project.

## Project Context

Project: {context.ProjectName}
Target Framework: {context.TargetFramework}
Files: {context.FileCount}
Root: {context.ProjectRoot}

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

    private class CodebaseContext
    {
        public string ProjectRoot { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public int FileCount { get; set; }
        public string Question { get; set; } = "";

        // TODO: Add relevant code snippets, types, methods, etc.
    }
}
