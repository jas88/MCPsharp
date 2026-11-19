# Analyzer Auto-Loading

MCPsharp automatically loads the Roslyn analyzers that the open project references, as an IDE does, without requiring explicit initialization. This eliminates the need for users to manually call `load_roslyn_analyzers` before using analysis features.

## Overview

### Before Auto-Loading
```
User: Run analyzers on my code
MCPsharp: No analyzers loaded, please call load_roslyn_analyzers first
User: [calls load_roslyn_analyzers with path]
User: [calls run_roslyn_analyzers]
MCPsharp: [returns analysis results]
```

### With Auto-Loading
```
User: Run analyzers on my code
MCPsharp: [auto-loads analyzers in background on first use]
MCPsharp: [returns analysis results immediately]
```

## How It Works

1. **Lazy Initialization**: Analyzers are auto-loaded on first use, not at startup
2. **Project Analyzers Only**: Loads the analyzers the open project's projects reference, and nothing else
3. **Automatic Registration**: Registers the loaded analyzers with the analyzer host
4. **Follows the Open Project**: Auto-load runs once per set of project analyzer references; after another project is opened, the next use replaces the previous project's analyzers
5. **Non-Blocking**: Failures in auto-load are logged but don't break MCPsharp

## Analyzer Sources

### 1. Project Analyzer References
The analyzers that the projects of the open workspace reference: the `Project.AnalyzerReferences` of each project loaded by MSBuildWorkspace, which are also what the compiler and an IDE use. These are:

- The analyzer assets of the NuGet packages the projects reference, at the versions the projects restore (e.g. **Microsoft.CodeAnalysis.NetAnalyzers**, **Roslynator.Analyzers**, **SonarAnalyzer.CSharp**)
- The analyzers the .NET SDK and targeting packs add (e.g. the .NET analyzers enabled by `EnableNETAnalyzers`)
- `<Analyzer Include="..." />` items and analyzer project references

Only the C# analyzers each reference exports with `[DiagnosticAnalyzer]` are loaded. Nothing outside these references is loaded: the NuGet cache is never scanned, so its size doesn't matter. The workspace has already loaded the referenced assemblies when it opened the project, and the auto-load reuses its analyzer instances. Each analyzer is identified by its type and the name and version of its assembly (see `excludeAnalyzers` below), so different builds of one analyzer, e.g. the SDK's and a NuGet package's code style analyzers or two versions of a package in different projects, are separate analyzers and are all loaded. They can then report the same diagnostic twice; pass `analyzer_ids` to `run_roslyn_analyzers` to run one of them. A reference to the same assembly identity at two paths is loaded once.

NuGet analyzer packages are only referenced once the projects are restored (`dotnet restore`). When MSBuildWorkspace cannot load the project and MCPsharp falls back to an adhoc workspace, there are no project analyzer references, so none are auto-loaded.

### 2. Built-In Providers
Auto-registers built-in code fix providers:
- **AsyncAwaitPatternProvider** - Async/await best practices
- **ExceptionLoggingProvider** - Exception handling improvements
- **UnusedCodeProvider** - Dead code detection
- **StaticMethodProvider** - Static method opportunities

### 3. Custom Paths
Load analyzers from custom directories or specific DLL files via configuration.

## Configuration

### Default Configuration
By default, auto-loading is enabled with these settings:
```json
{
  "autoLoadEnabled": true,
  "loadOnStartup": false,
  "analyzerSources": ["project_references", "builtin_providers"],
  "excludeAnalyzers": [],
  "customAnalyzerPaths": []
}
```

### Custom Configuration
`AnalyzerAutoLoadService` takes an `AnalyzerAutoLoadConfiguration`, and `AnalyzerAutoLoadService.LoadConfiguration(path)` reads one from a JSON file in this format (see `analyzer-autoload-schema.json`). The MCPsharp server currently starts the service with the default configuration and does not read a configuration file.

```json
{
  "autoLoadEnabled": true,
  "loadOnStartup": false,
  "analyzerSources": [
    "project_references",
    "builtin_providers"
  ],
  "excludeAnalyzers": [
    "Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers",
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers-10.0.403.0"
  ],
  "customAnalyzerPaths": [
    "/path/to/my/custom/analyzers",
    "/path/to/specific/MyAnalyzer.dll"
  ]
}
```

### Configuration Options

#### `autoLoadEnabled` (boolean, default: true)
Enable or disable auto-loading entirely. When `false`, users must manually call `load_roslyn_analyzers`.

#### `loadOnStartup` (boolean, default: false)
- `false` (recommended): Lazy load on first analyzer request (faster startup)
- `true`: Load all analyzers when MCPsharp starts (slower startup, instant first use)

The service does not act on this setting yet: analyzers are always loaded on first use.

#### `analyzerSources` (array of strings)
Sources to load analyzers from:
- `"project_references"` - The analyzers the open project's projects reference
- `"builtin_providers"` - Register built-in code fix providers

#### `excludeAnalyzers` (array of strings)
Analyzer IDs to exclude from auto-loading. Useful for disabling specific analyzers you don't want. An analyzer's ID, as `list_roslyn_analyzers` reports it, is `Roslyn_`, its full type name, `@`, then the name and version of its assembly, e.g. `Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers-10.0.403.0`. Leave out `-` and the version (`Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers`) to exclude the analyzer in every version of its assembly, so the exclusion still applies after the package is upgraded.

#### `customAnalyzerPaths` (array of strings)
Custom directories or DLL files to load analyzers from. Supports:
- Directory paths: Scans recursively for analyzer DLLs
- File paths: Loads specific analyzer DLL

Every DLL in a directory is loaded to look for analyzers, so a directory may hold at most 100 DLLs and 100 MB of DLLs (`AnalyzerDirectoryScanLimits`). A larger one, such as the NuGet cache, is skipped, and the auto-load result reports an error naming the limit; point at a specific package's analyzer folder (e.g. `~/.nuget/packages/<package>/<version>/analyzers/dotnet/cs`) or a single DLL instead.

## Performance

### Startup Impact
- **Lazy Loading (default)**: No impact on startup time

### First Use Latency
- **Without auto-load**: Requires manual load call + analysis time
- **With auto-load**: Auto-load + analysis time on first request. The workspace has already loaded the referenced analyzer assemblies, so the auto-load only wraps and registers their analyzers; its cost depends on what the projects reference, not on the size of the NuGet cache
- **Subsequent requests**: No auto-load overhead (cached until the project analyzer references change)

## Manual Loading Still Available

Auto-loading doesn't replace manual loading - you can still use `load_roslyn_analyzers` to:
- Load analyzers the open project doesn't reference
- Load analyzers from specific paths

After updating a project's analyzer packages, open the project again (`project_open`): the next analyzer request loads the new references.

## Troubleshooting

### Analyzers Not Loading
1. Check logs for auto-load errors (stderr)
2. Check that a project is open (`project_open`) and restored (`dotnet restore`): only the analyzers its projects reference are loaded
3. Check the workspace health for analyzer load failures (e.g. an analyzer built against a newer Roslyn than MCPsharp's)
4. Try manual load: call `load_roslyn_analyzers` with explicit path
5. Check configuration: ensure `autoLoadEnabled: true` and that `analyzerSources` contains `"project_references"`

### Performance Issues
1. Exclude analyzers you don't need via `excludeAnalyzers`
2. Disable auto-load and use manual loading for specific analyzers

### Configuration Not Applied
1. The MCPsharp server uses the default configuration; a configuration only applies when it is passed to `AnalyzerAutoLoadService`
2. Check JSON syntax validity

## Examples

### Minimal Configuration (Use Defaults)
No configuration file needed - auto-loading works out of the box.

### Disable Auto-Loading
```json
{
  "autoLoadEnabled": false
}
```

### Load Only Built-In Providers
```json
{
  "autoLoadEnabled": true,
  "analyzerSources": ["builtin_providers"]
}
```

### Custom Analyzers Only
```json
{
  "autoLoadEnabled": true,
  "analyzerSources": [],
  "customAnalyzerPaths": [
    "/path/to/my/analyzers"
  ]
}
```

### Exclude Noisy Analyzers
```json
{
  "autoLoadEnabled": true,
  "excludeAnalyzers": [
    "Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers",
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers-10.0.403.0"
  ]
}
```

## API Changes

### Before Auto-Loading
```typescript
// Required explicit load
await client.call("load_roslyn_analyzers", {
  path: "~/.nuget/packages/roslynator.analyzers/4.1.0/analyzers/dotnet/cs"
});
await client.call("run_roslyn_analyzers", { target_path: "/my/project" });
```

### With Auto-Loading
```typescript
// Direct analysis - auto-loads on first call
await client.call("run_roslyn_analyzers", { target_path: "/my/project" });
```

## Implementation Details

### Auto-Load Workflow
1. User calls `run_roslyn_analyzers` or `apply_roslyn_fixes`
2. `RoslynAnalyzerService` calls `AnalyzerAutoLoadService.EnsureAnalyzersLoadedAsync()`
3. The auto-load service gets the workspace's project analyzer references (`RoslynWorkspace.GetAnalyzerReferences()`, each assembly once); if they are the ones it last loaded, nothing more happens
4. Otherwise it unregisters the analyzers of its previous load, then loads the C# analyzers of the references (`RoslynAnalyzerLoader.LoadAnalyzersFromReferences()`) and any custom paths
5. Analyzers registered with `AnalyzerRegistry`, replacing the previous auto-loaded analyzers in `RoslynAnalyzerService` (explicitly loaded analyzers stay)
6. Analysis proceeds normally

### Thread Safety
- Auto-load is thread-safe (uses locking)
- Multiple concurrent requests trigger only one auto-load operation
- Subsequent requests wait for auto-load completion

### Error Handling
- Individual analyzer load failures are logged but don't fail auto-load
- Analyzer references that fail to load are logged as warnings (the workspace also reports them as analyzer load failures in its health)
- Auto-load failure doesn't crash MCPsharp - falls back to empty analyzer list

## Migration Guide

### For Existing Users
- Manual `load_roslyn_analyzers` calls still work
- The `nuget_cache` analyzer source was removed: MCPsharp no longer scans the NuGet cache (loading every analyzer there could exhaust memory and file handles). Use `project_references`, the default, which loads the analyzers the open project references
- `RoslynAnalyzerLoader.LoadAnalyzersFromNuGetCacheAsync()` and `RoslynCodeFixLoader.LoadCodeFixProvidersFromNuGetCacheAsync()` were removed; use `RoslynAnalyzerLoader.LoadAnalyzersFromReferences()` with a project's analyzer references

### For New Users
Simply use analyzer tools directly:
```bash
# No setup needed - just run analysis
mcpsharp run_roslyn_analyzers --target ./src
```

## Future Enhancements

Planned improvements:
- **Incremental loading**: Load analyzers in background after startup
- **Persistent cache**: Save discovered analyzers to disk for faster reload
- **Smart defaults**: Machine learning to predict which analyzers you'll use
