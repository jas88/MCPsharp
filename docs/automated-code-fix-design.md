# Automated Code Fix Design

**Version:** 1.0
**Date:** 2025-11-09
**Status:** Design Phase

## Executive Summary

This document describes the design for incorporating automated code quality fixes into MCPsharp as production-ready, reusable features. The system will leverage existing Roslyn analyzer infrastructure while adding built-in fix providers for common code quality patterns demonstrated during MCPsharp's own cleanup.

**Key Goals:**
- Make MCPsharp self-improving (dogfooding)
- Provide reusable fixes for any C# project analyzed via MCP
- Leverage existing infrastructure (RoslynCodeFixAdapter, FixEngine)
- Support batch operations across large codebases
- Enable safe, preview-first workflow

---

## 1. Background & Context

### 1.1 Current State

MCPsharp recently gained comprehensive Roslyn analyzer integration:
- **RoslynAnalyzerAdapter** - Wraps third-party analyzers
- **RoslynCodeFixAdapter** - Wraps third-party code fix providers
- **RoslynAnalyzerService** - Manages analyzer lifecycle
- **FixEngine** - Applies fixes with preview, conflict detection, rollback
- **8 MCP tools** - Exposed via JSON-RPC for Claude Code

### 1.2 Demonstrated Patterns

During MCPsharp cleanup, we created Python scripts demonstrating automated fixes:

**1. Exception Handling Patterns** (`fix_exception_logging.py`)
```python
# Before: Improper pattern
catch (Exception ex) { Logger.LogError(ex.Message); }

# After: Proper pattern
catch (Exception ex) { Logger.LogError(ex, "..."); }
```

**2. Async/Await Patterns** (`fix_async_warnings.py`, `bulk_fix_async.py`)
```python
# CS1998: Remove unnecessary async keyword
# Before: async Task<T> Method() { return value; }
# After: Task<T> Method() { return Task.FromResult(value); }
```

**3. Unused Code Removal** (demonstrated in cleanup)
```csharp
// Remove unused variables, fields, parameters
// Simplify unnecessary complexity
```

**4. Static Method Conversion** (demonstrated in cleanup)
```csharp
// Convert instance methods that don't use 'this' to static
```

### 1.3 Key Insights from Python Scripts

**Pattern Analysis** (from `fix_async_warnings.py`):
- Parse build warnings/errors for diagnostics
- Analyze method body for actual usage (e.g., presence of `await`)
- Categorize fixes: auto-applicable vs. needs-investigation
- Group by file for efficient batch processing
- Provide statistics and reporting

**Bulk Application** (from `bulk_fix_async.py`):
- Process files in reverse line order to maintain indices
- Simple text replacements for straightforward cases
- Flag complex cases requiring manual review
- Provide clear user feedback on what was/wasn't fixed

---

## 2. Architecture Design

### 2.1 Design Principles

1. **Leverage Existing Infrastructure** - Build on RoslynCodeFixAdapter, FixEngine
2. **Roslyn-First** - Use proper AST analysis, not text manipulation
3. **Safe Defaults** - Conservative automatic fixes, explicit user approval for aggressive ones
4. **Extensible** - Easy to add new fix patterns
5. **Observable** - Clear reporting, logging, metrics
6. **Testable** - Unit tests for each fix pattern

### 2.2 Implementation Options

#### Option A: Custom DiagnosticAnalyzers + CodeFixProviders

**Pros:**
- Standard Roslyn pattern
- IDE integration (if MCPsharp used in Rider/VS)
- Reusable outside MCPsharp
- Can be packaged as NuGet

**Cons:**
- More boilerplate
- Slower to develop
- Requires separate analyzer + fix provider classes

#### Option B: AST Rewriting Service (SyntaxRewriters)

**Pros:**
- Direct control over transformations
- Faster to implement simple patterns
- Can batch multiple fixes in single pass
- Good for bulk operations

**Cons:**
- Bypasses Roslyn's diagnostic infrastructure
- Harder to report issues incrementally
- Less reusable

#### **Option C: Hybrid Approach (RECOMMENDED)**

**Detection:** Use DiagnosticAnalyzers for precise issue identification
**Application:** Use SyntaxRewriters for efficient batch fixes
**Coordination:** FixEngine orchestrates preview, conflict detection, rollback

**Why Hybrid:**
- Best of both worlds
- Analyzers provide structured issue reporting
- Rewriters enable efficient bulk transformations
- Fits existing MCPsharp infrastructure
- Can still package analyzers as standalone NuGet

### 2.3 Proposed Class Hierarchy

```
Services/Analyzers/BuiltIn/CodeFixes/
├── Base/
│   ├── IAutomatedCodeFixProvider.cs         (interface)
│   ├── AutomatedCodeFixProviderBase.cs      (abstract base)
│   └── BatchSyntaxRewriterBase.cs           (base for rewriters)
│
├── Analyzers/
│   ├── ExceptionLoggingAnalyzer.cs          (DiagnosticAnalyzer)
│   ├── AsyncAwaitPatternAnalyzer.cs         (DiagnosticAnalyzer)
│   ├── UnusedCodeAnalyzer.cs                (DiagnosticAnalyzer)
│   └── StaticMethodAnalyzer.cs              (DiagnosticAnalyzer)
│
├── Fixers/
│   ├── ExceptionLoggingFixer.cs             (CodeFixProvider + Rewriter)
│   ├── AsyncAwaitPatternFixer.cs            (CodeFixProvider + Rewriter)
│   ├── UnusedCodeFixer.cs                   (CodeFixProvider + Rewriter)
│   └── StaticMethodFixer.cs                 (CodeFixProvider + Rewriter)
│
├── Registry/
│   ├── BuiltInCodeFixRegistry.cs            (discovery & registration)
│   └── CodeFixProfile.cs                    (fix profiles/presets)
│
└── Models/
    ├── FixProfile.cs                        (conservative, balanced, aggressive)
    ├── BatchFixOptions.cs                   (configuration)
    └── BatchFixResult.cs                    (results & stats)
```

---

## 3. Detailed Component Design

### 3.1 Base Interfaces & Classes

#### `IAutomatedCodeFixProvider.cs`

```csharp
public interface IAutomatedCodeFixProvider
{
    /// <summary>
    /// Unique identifier for this fix provider
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this fix does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Diagnostic IDs this provider can fix
    /// </summary>
    ImmutableArray<string> FixableDiagnosticIds { get; }

    /// <summary>
    /// Fix profile this belongs to (conservative, balanced, aggressive)
    /// </summary>
    FixProfile Profile { get; }

    /// <summary>
    /// Can this fix be applied automatically without user review?
    /// </summary>
    bool IsFullyAutomated { get; }

    /// <summary>
    /// Get the underlying DiagnosticAnalyzer
    /// </summary>
    DiagnosticAnalyzer GetAnalyzer();

    /// <summary>
    /// Get the underlying CodeFixProvider
    /// </summary>
    CodeFixProvider GetCodeFixProvider();

    /// <summary>
    /// Apply fixes in batch mode across multiple documents
    /// </summary>
    Task<BatchFixResult> ApplyBatchFixesAsync(
        ImmutableArray<Document> documents,
        BatchFixOptions options,
        IProgress<BatchFixProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

#### `AutomatedCodeFixProviderBase.cs`

```csharp
public abstract class AutomatedCodeFixProviderBase : IAutomatedCodeFixProvider
{
    protected ILogger Logger { get; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ImmutableArray<string> FixableDiagnosticIds { get; }
    public abstract FixProfile Profile { get; }
    public virtual bool IsFullyAutomated => false;

    protected AutomatedCodeFixProviderBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract DiagnosticAnalyzer GetAnalyzer();
    public abstract CodeFixProvider GetCodeFixProvider();

    public virtual async Task<BatchFixResult> ApplyBatchFixesAsync(
        ImmutableArray<Document> documents,
        BatchFixOptions options,
        IProgress<BatchFixProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Common batch fix logic:
        // 1. Run analyzer on all documents
        // 2. Collect diagnostics
        // 3. Group by file
        // 4. Apply rewriter in batch
        // 5. Report progress
        // 6. Generate statistics

        var startTime = DateTime.UtcNow;
        var results = new List<DocumentFixResult>();
        var totalDiagnostics = 0;
        var totalFixed = 0;

        for (int i = 0; i < documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var doc = documents[i];
            progress?.Report(new BatchFixProgress
            {
                CurrentFile = doc.FilePath ?? "Unknown",
                FilesProcessed = i,
                TotalFiles = documents.Length
            });

            var docResult = await ApplySingleDocumentFixAsync(doc, options, cancellationToken);
            results.Add(docResult);
            totalDiagnostics += docResult.DiagnosticsFound;
            totalFixed += docResult.DiagnosticsFixed;
        }

        return new BatchFixResult
        {
            Success = true,
            FixProviderId = Id,
            DocumentResults = results.ToImmutableArray(),
            TotalDiagnostics = totalDiagnostics,
            TotalFixed = totalFixed,
            Duration = DateTime.UtcNow - startTime
        };
    }

    protected abstract Task<DocumentFixResult> ApplySingleDocumentFixAsync(
        Document document,
        BatchFixOptions options,
        CancellationToken cancellationToken);
}
```

### 3.2 Specific Fix Implementations

#### Example: `AsyncAwaitPatternAnalyzer.cs`

```csharp
/// <summary>
/// Detects async methods that don't use await (CS1998)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncAwaitPatternAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MCP001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Async method lacks await",
        "Method '{0}' is marked async but contains no await expressions",
        "Async",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Remove async modifier and wrap return value with Task.FromResult.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;

        // Check if method is async
        if (!methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if method contains await
        var awaitExpressions = methodDecl.DescendantNodes()
            .OfType<AwaitExpressionSyntax>();

        if (awaitExpressions.Any())
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDecl.Identifier.GetLocation(),
            methodDecl.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }
}
```

#### Example: `AsyncAwaitPatternFixer.cs`

```csharp
/// <summary>
/// Fixes async methods that don't use await
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncAwaitPatternFixer))]
public class AsyncAwaitPatternFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncAwaitPatternAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDecl = root.FindToken(diagnosticSpan.Start)
            .Parent.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .First();

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove async and wrap return value",
                c => RemoveAsyncAndWrapReturnAsync(context.Document, methodDecl, c),
                nameof(AsyncAwaitPatternFixer)),
            diagnostic);
    }

    private async Task<Document> RemoveAsyncAndWrapReturnAsync(
        Document document,
        MethodDeclarationSyntax methodDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);

        // Remove async modifier
        var newModifiers = methodDecl.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.AsyncKeyword))
            .ToSyntaxTokenList();

        // Find return statements and wrap with Task.FromResult
        var rewriter = new AsyncReturnRewriter();
        var newMethod = (MethodDeclarationSyntax)rewriter.Visit(methodDecl);
        newMethod = newMethod.WithModifiers(newModifiers);

        var newRoot = root.ReplaceNode(methodDecl, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private class AsyncReturnRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression == null)
                return node; // return; (void)

            // Check if already wrapped
            if (node.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "FromResult")
            {
                return node; // Already wrapped
            }

            // Wrap with Task.FromResult(...)
            var wrappedExpr = SyntaxFactory.ParseExpression(
                $"Task.FromResult({node.Expression})");

            return node.WithExpression(wrappedExpr);
        }
    }
}
```

#### Example: `ExceptionLoggingAnalyzer.cs` + `ExceptionLoggingFixer.cs`

```csharp
/// <summary>
/// Detects improper exception logging patterns
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ExceptionLoggingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MCP002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Improper exception logging",
        "Use Logger.LogError(ex, message) instead of Logger.LogError(ex.Message)",
        "Logging",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Logging only ex.Message loses stack trace information.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if it's Logger.LogError or _logger.LogError
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "LogError")
            return;

        // Check if argument is ex.Message or exception.Message
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return;

        var firstArg = args[0].Expression;
        if (firstArg is MemberAccessExpressionSyntax argMember &&
            argMember.Name.Identifier.Text == "Message")
        {
            // Check if it's from an exception variable
            var semanticModel = context.SemanticModel;
            var symbolInfo = semanticModel.GetSymbolInfo(argMember.Expression);

            if (symbolInfo.Symbol is ILocalSymbol local &&
                IsExceptionType(local.Type))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, invocation.GetLocation()));
            }
        }
    }

    private bool IsExceptionType(ITypeSymbol? type)
    {
        if (type == null) return false;

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name == "Exception" &&
                current.ContainingNamespace.ToDisplayString() == "System")
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Fixes improper exception logging
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExceptionLoggingFixer))]
public class ExceptionLoggingFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExceptionLoggingAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];
        var invocation = root.FindNode(diagnostic.Location.SourceSpan) as InvocationExpressionSyntax;

        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use LogError(ex, message)",
                c => FixLoggingAsync(context.Document, invocation, c),
                nameof(ExceptionLoggingFixer)),
            diagnostic);
    }

    private async Task<Document> FixLoggingAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);

        // Extract exception variable from ex.Message
        var firstArg = invocation.ArgumentList.Arguments[0];
        var memberAccess = (MemberAccessExpressionSyntax)firstArg.Expression;
        var exceptionVar = memberAccess.Expression;

        // Build new argument list: (ex, "Error message")
        var newArgs = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(exceptionVar),
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("Error occurred")))
            }));

        var newInvocation = invocation.WithArgumentList(newArgs);
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }
}
```

### 3.3 Registry & Discovery

#### `BuiltInCodeFixRegistry.cs`

```csharp
/// <summary>
/// Central registry for built-in automated code fixes
/// </summary>
public class BuiltInCodeFixRegistry
{
    private readonly ILogger<BuiltInCodeFixRegistry> _logger;
    private readonly Dictionary<string, IAutomatedCodeFixProvider> _providers = new();

    public BuiltInCodeFixRegistry(ILogger<BuiltInCodeFixRegistry> logger)
    {
        _logger = logger;
        RegisterBuiltInProviders();
    }

    private void RegisterBuiltInProviders()
    {
        // Register all built-in fix providers
        RegisterProvider(new AsyncAwaitPatternFixProvider(_logger));
        RegisterProvider(new ExceptionLoggingFixProvider(_logger));
        RegisterProvider(new UnusedCodeFixProvider(_logger));
        RegisterProvider(new StaticMethodFixProvider(_logger));
    }

    public void RegisterProvider(IAutomatedCodeFixProvider provider)
    {
        _providers[provider.Id] = provider;
        _logger.LogInformation("Registered code fix provider: {Id} - {Name}",
            provider.Id, provider.Name);
    }

    public IAutomatedCodeFixProvider? GetProvider(string id)
    {
        _providers.TryGetValue(id, out var provider);
        return provider;
    }

    public ImmutableArray<IAutomatedCodeFixProvider> GetProvidersByProfile(FixProfile profile)
    {
        return _providers.Values
            .Where(p => p.Profile == profile)
            .ToImmutableArray();
    }

    public ImmutableArray<IAutomatedCodeFixProvider> GetAllProviders()
    {
        return _providers.Values.ToImmutableArray();
    }

    /// <summary>
    /// Get all analyzers from all providers
    /// </summary>
    public ImmutableArray<DiagnosticAnalyzer> GetAllAnalyzers()
    {
        return _providers.Values
            .Select(p => p.GetAnalyzer())
            .ToImmutableArray();
    }

    /// <summary>
    /// Get all code fix providers
    /// </summary>
    public ImmutableArray<CodeFixProvider> GetAllCodeFixProviders()
    {
        return _providers.Values
            .Select(p => p.GetCodeFixProvider())
            .ToImmutableArray();
    }
}
```

#### `CodeFixProfile.cs`

```csharp
/// <summary>
/// Predefined profiles for grouping fixes by aggressiveness
/// </summary>
public enum FixProfile
{
    /// <summary>
    /// Only safe, low-risk fixes (e.g., remove unused using statements)
    /// </summary>
    Conservative,

    /// <summary>
    /// Moderate risk fixes (e.g., simplify expressions, convert to expression body)
    /// </summary>
    Balanced,

    /// <summary>
    /// All fixes including structural changes (e.g., extract method, inline variable)
    /// </summary>
    Aggressive,

    /// <summary>
    /// Custom user-defined profile
    /// </summary>
    Custom
}

/// <summary>
/// Configuration for a fix profile
/// </summary>
public record FixProfileConfiguration
{
    public FixProfile Profile { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ImmutableArray<string> IncludedFixProviders { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> ExcludedFixProviders { get; init; } = ImmutableArray<string>.Empty;
    public bool RequireUserApproval { get; init; } = true;

    public static readonly FixProfileConfiguration ConservativeDefault = new()
    {
        Profile = FixProfile.Conservative,
        Name = "Conservative",
        Description = "Only apply safe, low-risk fixes",
        RequireUserApproval = false
    };

    public static readonly FixProfileConfiguration BalancedDefault = new()
    {
        Profile = FixProfile.Balanced,
        Name = "Balanced",
        Description = "Apply moderate-risk fixes with preview",
        RequireUserApproval = true
    };

    public static readonly FixProfileConfiguration AggressiveDefault = new()
    {
        Profile = FixProfile.Aggressive,
        Name = "Aggressive",
        Description = "Apply all available fixes (requires approval)",
        RequireUserApproval = true
    };
}
```

---

## 4. MCP Tool Integration

### 4.1 New MCP Tools

Add to `AnalyzerTools.cs`:

```csharp
new McpTool
{
    Name = "code_quality_analyze",
    Description = "Scan for code quality issues using built-in analyzers",
    InputSchema = JsonSerializer.SerializeToDocument(new
    {
        type = "object",
        properties = new
        {
            target_path = new { type = "string", description = "File or directory to analyze" },
            profile = new {
                type = "string",
                enum = new[] { "conservative", "balanced", "aggressive", "all" },
                description = "Fix profile to use"
            },
            include_providers = new {
                type = "array",
                items = new { type = "string" },
                description = "Specific fix providers to include"
            }
        },
        required = new[] { "target_path" }
    })
},
new McpTool
{
    Name = "code_quality_fix",
    Description = "Apply automated code quality fixes",
    InputSchema = JsonSerializer.SerializeToDocument(new
    {
        type = "object",
        properties = new
        {
            target_path = new { type = "string" },
            profile = new { type = "string", enum = new[] { "conservative", "balanced", "aggressive" } },
            fix_providers = new { type = "array", items = new { type = "string" } },
            preview_only = new { type = "boolean", description = "Preview changes without applying" },
            create_backup = new { type = "boolean", default = true },
            batch_mode = new { type = "boolean", description = "Process all files in batch" }
        },
        required = new[] { "target_path" }
    })
},
new McpTool
{
    Name = "code_quality_profiles",
    Description = "List available fix profiles and their configurations",
    InputSchema = JsonSerializer.SerializeToDocument(new
    {
        type = "object",
        properties = new
        {
            profile_name = new { type = "string", description = "Specific profile to get details for" }
        }
    })
}
```

### 4.2 Tool Implementation

```csharp
private static async Task<ToolCallResult> ExecuteCodeQualityAnalyze(
    JsonDocument arguments,
    BuiltInCodeFixRegistry registry,
    CancellationToken cancellationToken)
{
    var targetPath = arguments.RootElement.GetProperty("target_path").GetString() ?? string.Empty;
    var profileStr = arguments.RootElement.TryGetProperty("profile", out var p)
        ? p.GetString() : "balanced";

    var profile = Enum.Parse<FixProfile>(profileStr, ignoreCase: true);
    var providers = registry.GetProvidersByProfile(profile);

    // Run analyzers
    var allIssues = new List<AnalyzerIssue>();
    foreach (var provider in providers)
    {
        var analyzer = provider.GetAnalyzer();
        // Run analyzer logic...
    }

    // Group by fix provider
    var issuesByProvider = allIssues
        .GroupBy(i => i.RuleId)
        .ToDictionary(g => g.Key, g => g.ToList());

    return new ToolCallResult
    {
        Success = true,
        Result = JsonSerializer.Serialize(new
        {
            target_path = targetPath,
            profile = profileStr,
            total_issues = allIssues.Count,
            issues_by_provider = issuesByProvider.Select(kvp => new
            {
                provider_id = kvp.Key,
                count = kvp.Value.Count,
                issues = kvp.Value.Select(i => new
                {
                    file = i.FilePath,
                    line = i.LineNumber,
                    message = i.Description
                })
            }),
            available_fixes = providers.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                is_automated = p.IsFullyAutomated
            })
        })
    };
}

private static async Task<ToolCallResult> ExecuteCodeQualityFix(
    JsonDocument arguments,
    BuiltInCodeFixRegistry registry,
    FixEngine fixEngine,
    CancellationToken cancellationToken)
{
    var targetPath = arguments.RootElement.GetProperty("target_path").GetString() ?? string.Empty;
    var previewOnly = arguments.RootElement.TryGetProperty("preview_only", out var p) && p.GetBoolean();
    var batchMode = arguments.RootElement.TryGetProperty("batch_mode", out var b) && b.GetBoolean();

    // Load documents
    var documents = LoadDocumentsFromPath(targetPath);

    // Get fix providers
    var providers = GetRequestedProviders(arguments, registry);

    // Batch apply fixes
    var results = new List<BatchFixResult>();
    foreach (var provider in providers)
    {
        var options = new BatchFixOptions
        {
            PreviewOnly = previewOnly,
            CreateBackup = true,
            ValidateAfterApply = true
        };

        var result = await provider.ApplyBatchFixesAsync(
            documents,
            options,
            progress: new Progress<BatchFixProgress>(p =>
            {
                // Report progress via logging
            }),
            cancellationToken);

        results.Add(result);
    }

    return new ToolCallResult
    {
        Success = true,
        Result = JsonSerializer.Serialize(new
        {
            target_path = targetPath,
            preview_only = previewOnly,
            batch_mode = batchMode,
            providers_run = results.Count,
            total_diagnostics = results.Sum(r => r.TotalDiagnostics),
            total_fixed = results.Sum(r => r.TotalFixed),
            results = results.Select(r => new
            {
                provider_id = r.FixProviderId,
                diagnostics_found = r.TotalDiagnostics,
                diagnostics_fixed = r.TotalFixed,
                duration_ms = r.Duration.TotalMilliseconds,
                files_modified = r.DocumentResults.Count(d => d.Modified)
            })
        })
    };
}
```

---

## 5. Configuration & Profiles

### 5.1 Configuration File Format

`code-fix-profiles.json`:

```json
{
  "profiles": [
    {
      "name": "conservative",
      "description": "Safe, low-risk fixes only",
      "require_approval": false,
      "providers": [
        {
          "id": "AsyncAwaitPattern",
          "enabled": true,
          "options": {
            "only_simple_returns": true
          }
        },
        {
          "id": "UnusedCode",
          "enabled": true,
          "options": {
            "remove_unused_usings": true,
            "remove_unused_variables": false
          }
        }
      ]
    },
    {
      "name": "balanced",
      "description": "Moderate-risk fixes with preview",
      "require_approval": true,
      "providers": [
        {
          "id": "AsyncAwaitPattern",
          "enabled": true
        },
        {
          "id": "ExceptionLogging",
          "enabled": true
        },
        {
          "id": "UnusedCode",
          "enabled": true
        },
        {
          "id": "StaticMethod",
          "enabled": false
        }
      ]
    },
    {
      "name": "aggressive",
      "description": "All available fixes",
      "require_approval": true,
      "providers": [
        {
          "id": "*",
          "enabled": true
        }
      ]
    }
  ]
}
```

### 5.2 Loading Configuration

```csharp
public class CodeFixProfileManager
{
    private readonly Dictionary<string, FixProfileConfiguration> _profiles = new();

    public void LoadProfiles(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<CodeFixProfilesConfig>(json);

        foreach (var profile in config.Profiles)
        {
            _profiles[profile.Name] = profile;
        }
    }

    public FixProfileConfiguration? GetProfile(string name)
    {
        _profiles.TryGetValue(name, out var profile);
        return profile;
    }
}
```

---

## 6. Testing Strategy

### 6.1 Unit Tests

For each analyzer/fixer pair:

```csharp
[TestClass]
public class AsyncAwaitPatternFixerTests
{
    [TestMethod]
    public async Task RemoveAsync_SimpleReturn_WrapsWithTaskFromResult()
    {
        var code = @"
            public async Task<int> GetValue()
            {
                return 42;
            }
        ";

        var expected = @"
            public Task<int> GetValue()
            {
                return Task.FromResult(42);
            }
        ";

        await VerifyCodeFixAsync(code, expected);
    }

    [TestMethod]
    public async Task PreservesAsync_WhenAwaitPresent()
    {
        var code = @"
            public async Task<int> GetValue()
            {
                await Task.Delay(100);
                return 42;
            }
        ";

        await VerifyNoFixAsync(code);
    }

    private async Task VerifyCodeFixAsync(string code, string expected)
    {
        // Use Roslyn test helpers
        var analyzer = new AsyncAwaitPatternAnalyzer();
        var fixer = new AsyncAwaitPatternFixer();

        await RoslynTestHelpers.VerifyCodeFixAsync(analyzer, fixer, code, expected);
    }
}
```

### 6.2 Integration Tests

```csharp
[TestClass]
public class BatchFixIntegrationTests
{
    [TestMethod]
    public async Task ApplyBatchFixes_MultipleFiles_SuccessfullyApplies()
    {
        // Arrange
        var tempDir = CreateTempProjectWithIssues();
        var registry = new BuiltInCodeFixRegistry(logger);
        var provider = registry.GetProvider("AsyncAwaitPattern");

        // Act
        var result = await provider.ApplyBatchFixesAsync(
            LoadDocuments(tempDir),
            new BatchFixOptions { PreviewOnly = false },
            cancellationToken: default);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(10, result.TotalDiagnostics);
        Assert.AreEqual(10, result.TotalFixed);

        // Verify files actually changed
        VerifyFilesModified(tempDir, expectedChangeCount: 5);
    }
}
```

### 6.3 Performance Tests

```csharp
[TestClass]
public class FixPerformanceTests
{
    [TestMethod]
    public async Task BatchFix_500Files_CompletesUnder30Seconds()
    {
        var largeProject = GenerateProjectWithFiles(fileCount: 500);
        var stopwatch = Stopwatch.StartNew();

        var result = await ApplyAllFixesAsync(largeProject);

        stopwatch.Stop();
        Assert.IsTrue(stopwatch.Elapsed.TotalSeconds < 30);
        Assert.IsTrue(result.Success);
    }
}
```

---

## 7. Implementation Roadmap

### Phase 1: Foundation (Week 1)

**Goal:** Base infrastructure and first fix provider

- [x] Design document (this document)
- [ ] Create directory structure
- [ ] Implement `IAutomatedCodeFixProvider` interface
- [ ] Implement `AutomatedCodeFixProviderBase` abstract class
- [ ] Implement `BuiltInCodeFixRegistry`
- [ ] Implement `AsyncAwaitPatternAnalyzer` + `AsyncAwaitPatternFixer`
- [ ] Add unit tests for async/await fixer
- [ ] Document API usage

**Deliverable:** One working fix provider with tests

### Phase 2: Core Fixers (Week 2)

**Goal:** Implement remaining fix providers

- [ ] `ExceptionLoggingAnalyzer` + `ExceptionLoggingFixer`
- [ ] `UnusedCodeAnalyzer` + `UnusedCodeFixer`
- [ ] `StaticMethodAnalyzer` + `StaticMethodFixer`
- [ ] Unit tests for all fixers
- [ ] Integration tests for batch operations

**Deliverable:** Four working fix providers

### Phase 3: MCP Integration (Week 3)

**Goal:** Expose via MCP tools

- [ ] Add `code_quality_analyze` tool
- [ ] Add `code_quality_fix` tool
- [ ] Add `code_quality_profiles` tool
- [ ] Update `McpToolRegistry` to register new tools
- [ ] Create profiles configuration file
- [ ] Implement `CodeFixProfileManager`
- [ ] Add MCP integration tests

**Deliverable:** Three new MCP tools available to Claude Code

### Phase 4: Polish & Documentation (Week 4)

**Goal:** Production-ready release

- [ ] Performance testing & optimization
- [ ] Documentation (README, usage guide)
- [ ] Example workflows
- [ ] Self-apply to MCPsharp codebase (dogfooding)
- [ ] Metrics & reporting dashboard
- [ ] Error handling & edge cases
- [ ] Release notes

**Deliverable:** Production-ready feature

---

## 8. Usage Examples

### 8.1 Via MCP (Claude Code)

**Scenario:** Analyze a project for code quality issues

```json
{
  "method": "tools/call",
  "params": {
    "name": "code_quality_analyze",
    "arguments": {
      "target_path": "/Users/jas88/Developer/Github/MCPsharp/src",
      "profile": "balanced"
    }
  }
}
```

**Response:**
```json
{
  "result": {
    "total_issues": 42,
    "issues_by_provider": [
      {
        "provider_id": "AsyncAwaitPattern",
        "count": 15,
        "issues": [...]
      },
      {
        "provider_id": "ExceptionLogging",
        "count": 8,
        "issues": [...]
      }
    ],
    "available_fixes": [...]
  }
}
```

**Apply Fixes:**

```json
{
  "method": "tools/call",
  "params": {
    "name": "code_quality_fix",
    "arguments": {
      "target_path": "/Users/jas88/Developer/Github/MCPsharp/src",
      "profile": "balanced",
      "preview_only": false,
      "batch_mode": true
    }
  }
}
```

### 8.2 Programmatic Usage

```csharp
// Setup
var registry = new BuiltInCodeFixRegistry(logger);
var workspace = LoadWorkspace("/path/to/project");

// Analyze
var provider = registry.GetProvider("AsyncAwaitPattern");
var documents = workspace.CurrentSolution.Projects
    .SelectMany(p => p.Documents)
    .ToImmutableArray();

// Apply fixes
var result = await provider.ApplyBatchFixesAsync(
    documents,
    new BatchFixOptions
    {
        PreviewOnly = false,
        CreateBackup = true,
        ValidateAfterApply = true
    },
    progress: new Progress<BatchFixProgress>(p =>
    {
        Console.WriteLine($"Processing {p.CurrentFile}... {p.FilesProcessed}/{p.TotalFiles}");
    }));

// Report
Console.WriteLine($"Fixed {result.TotalFixed} of {result.TotalDiagnostics} issues");
Console.WriteLine($"Modified {result.DocumentResults.Count(d => d.Modified)} files");
```

### 8.3 Self-Improvement Workflow

```bash
# MCPsharp analyzing itself
dotnet run --project src/MCPsharp -- \
  code-quality analyze \
  --target src/MCPsharp \
  --profile balanced \
  --output analysis-report.json

# Review report
cat analysis-report.json | jq '.issues_by_provider'

# Apply fixes
dotnet run --project src/MCPsharp -- \
  code-quality fix \
  --target src/MCPsharp \
  --profile conservative \
  --batch \
  --backup \
  --validate

# Commit changes
git add -p  # Review changes interactively
git commit -m "Apply automated code quality fixes (conservative profile)"
```

---

## 9. Edge Cases & Safety

### 9.1 Ambiguous Cases

**Problem:** Some patterns require human judgment

**Examples:**
- `async` method with conditional await (only awaits in some branches)
- Exception logging where message is intentionally logged separately
- Static method conversion where instance method is overridden in subclass

**Solution:**
- Mark as `IsFullyAutomated = false`
- Require user approval
- Provide detailed explanation in fix description
- Offer "skip" option

### 9.2 Breaking Changes

**Problem:** Fix might change semantics

**Example:**
```csharp
// Before: async Task<int> GetValue() { return 42; }
// After:  Task<int> GetValue() { return Task.FromResult(42); }

// If called with:
var result = await GetValue().ConfigureAwait(false);

// Behavior changes: sync execution vs. posted to thread pool
```

**Solution:**
- Semantic analysis before applying fix
- Warn about potential behavior changes
- Document breaking changes in fix description
- Conservative profile excludes risky fixes

### 9.3 Conflict Detection

**Problem:** Multiple fixes might conflict

**Example:**
- Fix A: Remove unused variable `x`
- Fix B: Extract method that references `x`

**Solution:**
- Leverage existing `FixEngine.DetectConflictsAsync`
- Order fixes by dependency (apply extractions before removals)
- Detect overlapping edits
- Re-analyze after each batch

### 9.4 Performance Degradation

**Problem:** Batch fixes on large codebases might be slow

**Mitigation:**
- Parallel processing where safe
- Progress reporting
- Cancellation support
- Incremental re-analysis
- Cache compilation results

---

## 10. Success Metrics

### 10.1 Quantitative Metrics

- **Fix Accuracy:** % of fixes that compile successfully
- **Coverage:** % of diagnostic IDs covered by fixers
- **Performance:** Time to analyze/fix 1000 files
- **Adoption:** # of times tools used per week
- **Self-Improvement:** # of fixes applied to MCPsharp itself

**Targets:**
- Fix accuracy: >99%
- Coverage: 80% of common warnings
- Performance: <30s for 500 files
- Self-improvement: Apply to MCPsharp monthly

### 10.2 Qualitative Metrics

- User feedback on fix quality
- Reduction in manual code review comments
- Developer satisfaction
- CI/CD integration success

---

## 11. Future Enhancements

### 11.1 Additional Fix Providers

**High Priority:**
- **Nullable reference types** - Add missing `?` annotations
- **Pattern matching** - Simplify `is` checks
- **Collection expressions** - Use C# 12 collection syntax
- **Primary constructors** - Convert to C# 12 syntax

**Medium Priority:**
- **String interpolation** - Replace `string.Format`
- **LINQ optimization** - Replace loops with LINQ
- **File-scoped namespaces** - Convert to C# 10 syntax
- **Records** - Suggest record types for data classes

**Low Priority:**
- **Extract method** - Suggest method extraction
- **Inline variable** - Suggest inlining single-use vars
- **Rename** - Suggest better naming

### 11.2 IDE Integration

- Visual Studio extension
- Rider plugin
- VS Code extension (via OmniSharp)

### 11.3 NuGet Package

Package as standalone analyzer NuGet:
- `MCPsharp.Analyzers` - Analyzer DLLs
- `MCPsharp.CodeFixes` - Fix provider DLLs
- Can be used outside MCPsharp

### 11.4 AI-Assisted Fixes

- Use Claude to generate fix descriptions
- Suggest fixes for custom warnings
- Learn from user corrections

---

## 12. Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Incorrect fixes break code | High | Medium | Extensive testing, preview mode, backups |
| Performance issues on large codebases | Medium | Medium | Parallel processing, cancellation, progress |
| User confusion about profiles | Low | High | Clear documentation, sensible defaults |
| Conflicts with existing analyzers | Medium | Low | Namespace isolation, unique diagnostic IDs |
| Maintenance burden | Medium | High | Comprehensive tests, clear architecture |

---

## 13. Appendix

### 13.1 Diagnostic ID Ranges

**Reserved for MCPsharp:**
- `MCP001-MCP099`: Code quality fixes
- `MCP100-MCP199`: Migration analyzers
- `MCP200-MCP299`: Performance analyzers
- `MCP300-MCP399`: Security analyzers

**Current Assignments:**
- `MCP001`: Async method lacks await
- `MCP002`: Improper exception logging
- `MCP003`: Unused variable/field
- `MCP004`: Method can be static

### 13.2 References

**Roslyn Analyzer Docs:**
- [Writing Roslyn Analyzers](https://github.com/dotnet/roslyn/blob/main/docs/wiki/How-To-Write-a-C%23-Analyzer-and-Code-Fix.md)
- [Roslyn API Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)

**Existing Analyzers:**
- [Roslynator](https://github.com/JosefPihrt/Roslynator)
- [StyleCop Analyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers)
- [ErrorProne.NET](https://github.com/SergeyTeplyakov/ErrorProne.NET)

**MCPsharp Docs:**
- `/docs/tool-consolidation-design.md`
- `/docs/efficiency-analysis-report.md`
- `/docs/improvement-roadmap.md`

### 13.3 Code Examples Repository

All example code and tests available at:
`/Users/jas88/Developer/Github/MCPsharp/examples/automated-fixes/`

---

## 14. Approval & Sign-Off

**Document Version:** 1.0
**Last Updated:** 2025-11-09
**Next Review:** After Phase 1 completion

**Reviewers:**
- [ ] Architecture approval
- [ ] Implementation team review
- [ ] Testing strategy approval
- [ ] Documentation review

---

**End of Design Document**
