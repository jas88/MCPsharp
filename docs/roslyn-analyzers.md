# Roslyn Analyzer Integration

MCPsharp now includes comprehensive Roslyn analyzer integration, providing both self-improvement capabilities and MCP tools for analyzing target C# projects.

## Overview

The Roslyn analyzer integration serves two purposes:

1. **Self-Improvement**: Roslyn analyzers run during MCPsharp's own build process to catch code quality issues
2. **MCP Tool Capability**: MCPsharp can load and run Roslyn analyzers on target C# projects, exposing results through MCP

## Architecture

### Component Hierarchy

```
┌─────────────────────────────────────┐
│ RoslynAnalyzerService               │
│ (High-level orchestration)          │
└─────────────────┬───────────────────┘
                  │
    ┌─────────────┼─────────────┐
    │             │             │
    ▼             ▼             ▼
┌────────┐  ┌──────────┐  ┌──────────────┐
│Analyzer│  │Analyzer  │  │Analyzer      │
│Loader  │  │Adapter   │  │Host          │
└────────┘  └──────────┘  └──────────────┘
```

### Key Components

#### 1. RoslynAnalyzerAdapter
**File**: `src/MCPsharp/Services/Analyzers/RoslynAnalyzerAdapter.cs`

Wraps Roslyn's `DiagnosticAnalyzer` to implement MCPsharp's `IAnalyzer` interface. This allows Roslyn analyzers to work seamlessly with MCPsharp's analyzer framework.

**Key Features**:
- Converts Roslyn diagnostics to MCPsharp issues
- Creates mini-compilations for analysis
- Maps severity levels and categories
- Provides metadata about analyzer rules

**Example Usage**:
```csharp
var roslynAnalyzer = new SomeRoslynAnalyzer();
var adapter = new RoslynAnalyzerAdapter(roslynAnalyzer, logger);

var result = await adapter.AnalyzeAsync(filePath, content, cancellationToken);
// result.Issues contains MCPsharp.Models.Analyzers.AnalyzerIssue objects
```

#### 2. RoslynAnalyzerLoader
**File**: `src/MCPsharp/Services/Analyzers/RoslynAnalyzerLoader.cs`

Discovers and loads Roslyn analyzer assemblies from:
- Individual assembly files
- Directories (recursive search)
- NuGet package cache

**Key Features**:
- Assembly reflection to find `DiagnosticAnalyzer` types
- Automatic discovery in directories
- NuGet cache scanning
- Assembly metadata extraction

**Example Usage**:
```csharp
var loader = new RoslynAnalyzerLoader(logger, loggerFactory);

// Load from specific assembly
var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(
    "/path/to/analyzer.dll",
    cancellationToken);

// Discover assemblies in directory
var assemblyPaths = loader.DiscoverAnalyzerAssemblies(
    "/path/to/analyzers/",
    recursive: true);

// Load from NuGet cache
var nugetAnalyzers = await loader.LoadAnalyzersFromNuGetCacheAsync(
    cancellationToken);
```

#### 3. RoslynAnalyzerService
**File**: `src/MCPsharp/Services/Analyzers/RoslynAnalyzerService.cs`

High-level orchestration service that manages analyzer lifecycle and execution.

**Key Features**:
- Centralized analyzer management
- Batch loading and execution
- Integration with MCPsharp's analyzer host
- Progress tracking

**Example Usage**:
```csharp
var service = new RoslynAnalyzerService(loader, analyzerHost, logger);

// Load analyzers from directory
await service.LoadAnalyzersFromDirectoryAsync("/analyzers");

// Run all loaded analyzers on target
var result = await service.RunAnalyzersAsync("/path/to/project");

// Run specific analyzers
var result = await service.RunAnalyzersAsync(
    "/path/to/project",
    analyzerIds: new[] { "CA1001", "CA2007" });
```

## Self-Improvement: Analyzers in MCPsharp Build

### Installed Analyzer Packages

MCPsharp includes the following analyzer packages (configured in `MCPsharp.csproj`):

1. **Microsoft.CodeAnalysis.Analyzers** (3.11.0)
   - Core Roslyn analyzer infrastructure
   - API usage best practices

2. **Microsoft.CodeAnalysis.NetAnalyzers** (9.0.0)
   - .NET API usage analyzers
   - Performance, security, reliability rules
   - Code quality analyzers (CAxxxx rules)

3. **Microsoft.CodeAnalysis.CSharp.CodeStyle** (4.11.0)
   - C# code style analyzers
   - Formatting and naming conventions
   - Language usage patterns

4. **Roslynator.Analyzers** (4.12.11)
   - Extensive collection of analyzers (1000+ rules)
   - Performance optimizations
   - Code simplifications
   - Best practices

5. **SonarAnalyzer.CSharp** (10.5.0)
   - Code quality and security
   - Bug detection
   - Code smell detection

6. **ErrorProne.NET.CoreAnalyzers** (0.6.1-beta.1)
   - Common error patterns
   - Structural issues

7. **ErrorProne.NET.Structs** (0.6.1-beta.1)
   - Struct usage best practices
   - Performance issues with structs

### Configuration

Analyzers are configured with `PrivateAssets="all"` to:
- Keep analyzer DLLs out of published output
- Prevent analyzer dependencies from leaking
- Ensure analyzers only run during build

Example configuration:
```xml
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

### Suppressing Warnings

To suppress specific analyzer warnings, use:

**Global suppression** (in `.globalconfig` or `Directory.Build.props`):
```ini
dotnet_diagnostic.CA1062.severity = none
```

**Local suppression** (in code):
```csharp
#pragma warning disable CA1062
// Code here
#pragma warning restore CA1062
```

**Attribute-based suppression**:
```csharp
[SuppressMessage("Design", "CA1062:Validate arguments of public methods")]
public void Method(string arg)
{
    // ...
}
```

## MCP Tool Integration (Future)

While the infrastructure is in place, MCP tool registration is pending. The planned tools include:

### `load_roslyn_analyzers`
Load Roslyn analyzers from assembly or directory.

**Parameters**:
- `path` (string, required): Path to assembly or directory
- `recursive` (boolean, optional): Search recursively (default: true)

**Returns**:
```json
{
  "analyzers_loaded": 15,
  "analyzer_ids": ["Roslyn_CA1001", "Roslyn_CA2007", ...],
  "assembly_path": "/path/to/assembly.dll"
}
```

### `run_roslyn_analyzers`
Run loaded Roslyn analyzers on target path.

**Parameters**:
- `target_path` (string, required): File or directory to analyze
- `analyzer_ids` (string[], optional): Specific analyzers to run (default: all)

**Returns**:
```json
{
  "success": true,
  "analyzers_run": 15,
  "issues_found": 42,
  "issues": [
    {
      "rule_id": "CA1001",
      "file_path": "/path/to/file.cs",
      "line": 123,
      "column": 5,
      "severity": "Warning",
      "message": "Type owns disposable fields but is not disposable"
    }
  ]
}
```

### `list_roslyn_analyzers`
List all loaded Roslyn analyzers.

**Returns**:
```json
{
  "analyzers": [
    {
      "id": "Roslyn_CA1001",
      "name": "CA1001",
      "description": "Types that own disposable fields should be disposable",
      "rules_count": 1
    }
  ]
}
```

## Usage Examples

### Example 1: Load and Run Analyzers

```csharp
// Initialize services
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var loader = new RoslynAnalyzerLoader(
    loggerFactory.CreateLogger<RoslynAnalyzerLoader>(),
    loggerFactory);
var analyzerHost = // ... (configured analyzer host)
var service = new RoslynAnalyzerService(loader, analyzerHost, logger);

// Load analyzers from Roslynator package
await service.LoadAnalyzersFromDirectoryAsync(
    "~/.nuget/packages/roslynator.analyzers/4.12.11/analyzers");

// Run on target project
var result = await service.RunAnalyzersAsync("~/projects/MyApp/src");

Console.WriteLine($"Analyzed with {result.AnalyzersRun.Length} analyzers");
Console.WriteLine($"Found {result.TotalIssuesFound} issues");
```

### Example 2: Custom Analyzer Integration

```csharp
// Create custom Roslyn analyzer
public class CustomAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }

    public override void Initialize(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        // Analysis logic
    }
}

// Wrap in adapter
var customAnalyzer = new CustomAnalyzer();
var adapter = new RoslynAnalyzerAdapter(customAnalyzer, logger);

// Use with MCPsharp's analyzer framework
var result = await adapter.AnalyzeAsync(filePath, content, cancellationToken);
```

## Benefits

### For MCPsharp Development

1. **Automatic Code Quality Enforcement**: Analyzers catch issues during build
2. **Consistent Code Style**: Enforced naming, formatting, and patterns
3. **Security Scanning**: Identify security vulnerabilities early
4. **Performance Optimization**: Detect performance anti-patterns

### For Target Projects

1. **Comprehensive Analysis**: Run industry-standard analyzers on any C# project
2. **Customizable**: Load any Roslyn analyzer assembly
3. **Extensible**: Easy to add custom analyzers
4. **Integrated**: Results available through MCP for Claude Code

## Limitations

1. **Compilation Required**: Analyzers need compilable code for accurate results
2. **Performance**: Large codebases may take time to analyze
3. **Memory Usage**: Roslyn compilations are memory-intensive
4. **Single-File Apps**: Assembly.Location returns empty in published single-file apps (warning IL3000)

## Future Enhancements

1. **Code Fix Integration**: Support Roslyn `CodeFixProvider` for automatic fixes
2. **Incremental Analysis**: Cache results and only re-analyze changed files
3. **Parallel Execution**: Run multiple analyzers concurrently
4. **Custom Rules**: Easy DSL for defining custom analysis rules
5. **MCP Tool Registration**: Complete integration with McpToolRegistry
6. **Configuration UI**: Web-based analyzer configuration
7. **Reporting**: Generate HTML/PDF reports from analysis results

## Troubleshooting

### Issue: "Assembly could not be loaded"

**Cause**: Target framework mismatch or missing dependencies

**Solution**: Ensure analyzer assembly targets compatible framework (.NET 6+)

### Issue: "No analyzers found in assembly"

**Cause**: Assembly doesn't contain `DiagnosticAnalyzer` types

**Solution**: Verify assembly is an analyzer package (look for `DiagnosticAnalyzer` implementations)

### Issue: "Analysis takes too long"

**Cause**: Large codebase or complex analyzers

**Solution**:
- Run fewer analyzers at once
- Use specific analyzer IDs instead of all
- Analyze smaller subsets of files

### Issue: IL3000 warnings about Assembly.Location

**Cause**: Single-file app deployment incompatibility

**Solution**: These warnings are expected and don't affect functionality. To suppress:
```xml
<PropertyGroup>
  <NoWarn>IL3000</NoWarn>
</PropertyGroup>
```

## References

- [Roslyn Analyzers Overview](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview)
- [Writing Custom Analyzers](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- [Roslynator Documentation](https://github.com/JosefPihrt/Roslynator)
- [.NET Code Analysis Rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)
