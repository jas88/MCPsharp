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

**Analyzer IDs**: an adapter's ID is `Roslyn_`, the analyzer's full type name, `@` and its assembly's name, then `+` and the assembly's culture unless it is neutral, `+` and its public key token if it is strong-named, and `-` and its version, e.g. `Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0` (`RoslynAnalyzerAdapter.GetId`). The ID is the same in every run and names the whole identity of the analyzer's assembly, so analyzers in assemblies of different identities get different IDs: same-named analyzers in different assemblies, different builds of one analyzer (such as the SDK's and a NuGet package's code style analyzers), and builds signed with different keys. Assemblies of the same identity, such as two unsigned builds with the same name and version, give the same IDs, and their analyzers count as one. `VersionIndependentId` is the ID without `-` and the version; `excludeAnalyzers` accepts either form. `name` is the analyzer's type name.

**Example Usage**:
```csharp
var roslynAnalyzer = new SomeRoslynAnalyzer();
var adapter = new RoslynAnalyzerAdapter(roslynAnalyzer, logger);

var result = await adapter.AnalyzeAsync(filePath, content, cancellationToken);
// result.Issues contains MCPsharp.Models.Analyzers.AnalyzerIssue objects
```

#### 2. RoslynAnalyzerLoader
**File**: `src/MCPsharp/Services/Analyzers/RoslynAnalyzerLoader.cs`

Loads Roslyn analyzers from:
- The analyzer references of the projects being analysed (`Project.AnalyzerReferences`), as an IDE does
- Individual assembly files
- Directories (recursive search)

It never scans the NuGet cache: the analyzers of a project come from that project's references, which are the analyzer assets of the packages it restores (at the restored versions), the SDK's analyzers and any `Analyzer` items. `AnalyzerAutoLoadService` uses this to load the analyzers of the open project automatically (see [analyzer-autoload.md](analyzer-autoload.md)).

**Key Features**:
- Project analyzer references load through Roslyn, so only the types they export for C# with `[DiagnosticAnalyzer]` are used, and a workspace's already-loaded analyzer instances are reused
- Assembly reflection to find `DiagnosticAnalyzer` types in explicitly given assemblies
- Automatic discovery in directories
- Assembly metadata extraction

**Example Usage**:
```csharp
var loader = new RoslynAnalyzerLoader(logger, loggerFactory);

// Load the analyzers the projects of the open workspace reference
var projectAnalyzers = loader.LoadAnalyzersFromReferences(
    roslynWorkspace.GetAnalyzerReferences(),
    cancellationToken);

// Load from specific assembly
var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(
    "/path/to/analyzer.dll",
    cancellationToken);

// Discover assemblies in directory
var assemblyPaths = loader.DiscoverAnalyzerAssemblies(
    "/path/to/analyzers/",
    recursive: true);
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
    analyzerIds: new[] { "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0" });
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

## MCP Tool Integration

The following tools are registered whenever the server is started with a Roslyn analyzer service (the default):

### `load_roslyn_analyzers`
Load Roslyn analyzers from assembly or directory.

Every DLL in a directory is loaded to look for analyzers, so the directory may hold at most 100 DLLs and 100 MB of DLLs. For a larger one, such as the NuGet cache, the tool returns an error naming the limit and suggesting a specific package's analyzer folder (e.g. `~/.nuget/packages/<package>/<version>/analyzers/dotnet/cs`), a project's analyzer output folder, or a single DLL.

**Parameters**:
- `path` (string, required): Path to an analyzer assembly or a directory of assemblies (relative paths resolve against the open project)

**Returns**:
```json
{
  "analyzersLoaded": 1,
  "analyzers": [
    {
      "id": "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0",
      "name": "TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer",
      "description": "Roslyn analyzer: TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer",
      "version": "10.0.403.0",
      "author": "Microsoft Corporation",
      "supportedExtensions": [".cs"],
      "isEnabled": true,
      "rules": null
    }
  ]
}
```

### `run_roslyn_analyzers`
Run Roslyn analyzers on target path. As in an IDE, each file gets the analyzers of the projects that compile it, which are loaded automatically, plus any loaded with `load_roslyn_analyzers` or from `customAnalyzerPaths`, which run on every file; a file in no project gets only the latter. A file several projects compile (linked, or multi-targeted) gets each of their analyzers once, and the same diagnostic from more than one of them is reported once. See [analyzer-autoload.md](analyzer-autoload.md#which-analyzers-run-on-a-file).

**Parameters**:
- `target_path` (string, required): File or directory to analyze; for a directory, its C# files outside `bin`, `obj` and hidden directories, in path order and at most 100
- `analyzer_ids` (string[], optional): Specific analyzers to run, by the IDs `list_roslyn_analyzers` reports (default: all); each file still gets only those that apply to it

**Returns**:
```json
{
  "analyzersRun": [
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0",
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0"
  ],
  "filesAnalyzed": 12,
  "filesSkipped": 0,
  "totalIssuesFound": 42,
  "durationSeconds": 1.84,
  "issues": [
    {
      "ruleId": "CA1001",
      "analyzerId": "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0",
      "severity": "Warning",
      "message": "Type owns disposable fields but is not disposable",
      "filePath": "/path/to/file.cs",
      "line": 123,
      "column": 5
    }
  ]
}
```

Each issue's `ruleId` is the diagnostic ID that reported it (e.g. `CA1001`), which `apply_roslyn_fixes` takes in `diagnostic_ids`. `filesAnalyzed` counts the files at least one analyzer ran on, and `filesSkipped` the files of a directory beyond the first 100.

A failed run is reported as a tool error rather than in the result.

### `list_roslyn_analyzers`
List all loaded Roslyn analyzers.

**Returns**:
```json
{
  "totalAnalyzers": 1,
  "analyzers": [
    {
      "id": "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0",
      "name": "TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer",
      "description": "Roslyn analyzer: TypesThatOwnDisposableFieldsShouldBeDisposableAnalyzer",
      "version": "10.0.403.0",
      "author": "Microsoft Corporation",
      "supportedExtensions": [".cs"],
      "isEnabled": true,
      "rules": [
        {
          "id": "CA1001",
          "title": "Types that own disposable fields should be disposable",
          "category": "Design",
          "severity": "Warning"
        }
      ]
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
