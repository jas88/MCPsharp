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

1. **Lazy Initialization**: Analyzers are auto-loaded on first use, not at startup (unless the project's configuration sets `loadOnStartup`)
2. **Project Analyzers Only**: Loads the analyzers the open project's projects reference, and nothing else
3. **Automatic Registration**: Registers the loaded analyzers with the analyzer host
4. **Follows the Open Project**: Auto-load runs once per set of project analyzer references and configuration; after another project is opened, or its configuration file changes, the next use replaces the previous analyzers
5. **Non-Blocking**: Failures in auto-load are logged but don't break MCPsharp

## Analyzer Sources

### 1. Project Analyzer References
The analyzers that the projects of the open workspace reference: the `Project.AnalyzerReferences` of each project loaded by MSBuildWorkspace, which are also what the compiler and an IDE use. These are:

- The analyzer assets of the NuGet packages the projects reference, at the versions the projects restore (e.g. **Microsoft.CodeAnalysis.NetAnalyzers**, **Roslynator.Analyzers**, **SonarAnalyzer.CSharp**)
- The analyzers the .NET SDK and targeting packs add (e.g. the .NET analyzers enabled by `EnableNETAnalyzers`)
- `<Analyzer Include="..." />` items and analyzer project references

Only the C# analyzers each reference exports with `[DiagnosticAnalyzer]` are loaded. Nothing outside these references is loaded: the NuGet cache is never scanned, so its size doesn't matter. The workspace has already loaded the referenced assemblies when it opened the project, and the auto-load reuses its analyzer instances. Each analyzer is identified by its type and its assembly's identity: name, culture, public key token and version (see `excludeAnalyzers` below), so different builds of one analyzer, e.g. the SDK's and a NuGet package's code style analyzers or two versions of a package in different projects, are separate analyzers and are all loaded. Each runs only on the files of the projects that reference it (see [Which Analyzers Run on a File](#which-analyzers-run-on-a-file)), so a file doesn't get the same diagnostic from two builds. When projects reference copies of one assembly at different paths (e.g. two restores of a package), each copy is loaded, and their analyzers, which have the same IDs, are registered once: each runs on the files of every project that references one of the copies. An analyzer from `customAnalyzerPaths` with the ID of a project's analyzer is the one registered, since it runs on every file.

### Which Analyzers Run on a File
`run_roslyn_analyzers` and `apply_roslyn_fixes` pick the analyzers for each file as an IDE does (`IRoslynAnalyzerService.GetAnalyzersForFile`):

- A file gets the analyzers of the projects that compile it (`RoslynWorkspace.GetFileAnalyzerScope`). Every target framework of a multi-targeted project counts as a project, as does every project a linked file belongs to.
- Analyzers that belong to no project, those loaded with `load_roslyn_analyzers` or from `customAnalyzerPaths`, run on every file.
- A file in no project of the open workspace (or when no project is open) gets only the analyzers that belong to no project.
- A file that several projects compile runs each of their analyzers once, and a diagnostic that more than one of them reports with the same rule, location and message is reported once. In a file of one project, identical diagnostics from different analyzers are all reported.
- `analyzer_ids` narrows the analyzers to run; it never adds an analyzer that doesn't apply to a file. If none of the requested analyzers applies, the run fails and says why.
- A directory target runs on its C# files outside build output (`bin`, `obj`) and hidden directories, in path order and at most 100 of them; `filesSkipped` counts the rest. Each file gets the analyzers of its own projects.

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
Create `.mcpsharp/autoload-config.json` in the project root: the directory of the solution or project file MCPsharp opened, or the folder given to `project_open` (`AnalyzerAutoLoadService.GetConfigurationPath`). The format is in `analyzer-autoload-schema.json`; properties you leave out keep their defaults.

- MCPsharp reads the file when it next auto-loads analyzers, and again whenever the file changes, so no restart is needed; each project has its own configuration.
- Without the file, the default configuration applies. A file that cannot be read or parsed is ignored in favour of the default configuration, with a warning in the log (stderr) and in the auto-load errors.
- Relative `customAnalyzerPaths` are resolved against the project root.
- Unknown `analyzerSources` (such as `nuget_cache`, which MCPsharp no longer has) are reported and ignored.

```json
{
  "autoLoadEnabled": true,
  "loadOnStartup": false,
  "analyzerSources": [
    "project_references",
    "builtin_providers"
  ],
  "excludeAnalyzers": [
    "Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35",
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0"
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
- `false` (recommended): Lazy load on first analyzer request
- `true`: Load the project's analyzers as soon as MCPsharp has loaded the project at startup. The load runs in the background, so MCPsharp answers the MCP handshake at once; an analyzer request that arrives before the load finishes waits for it

The setting applies to the project MCPsharp opens when it starts. A project opened later with `project_open` loads its analyzers on first use.

#### `analyzerSources` (array of strings)
Sources to load analyzers from:
- `"project_references"` - The analyzers the open project's projects reference
- `"builtin_providers"` - Register built-in code fix providers

#### `excludeAnalyzers` (array of strings)
Analyzer IDs to exclude from auto-loading. Useful for disabling specific analyzers you don't want. An analyzer's ID, as `list_roslyn_analyzers` reports it, is `Roslyn_`, its full type name, `@` and its assembly's name, then `+` and the assembly's culture unless it is neutral, `+` and its public key token if it is strong-named, and `-` and its version, e.g. `Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0`. Leave out `-` and the version (`Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35`) to exclude the analyzer in every version of its assembly, so the exclusion still applies after the package is upgraded.

#### `customAnalyzerPaths` (array of strings)
Custom directories or DLL files to load analyzers from. Supports:
- Directory paths: Scans recursively for analyzer DLLs
- File paths: Loads specific analyzer DLL

Every DLL in a directory is loaded to look for analyzers, so a directory may hold at most 100 DLLs and 100 MB of DLLs (`AnalyzerDirectoryScanLimits`). A larger one, such as the NuGet cache, is skipped, and the auto-load result reports an error naming the limit; point at a specific package's analyzer folder (e.g. `~/.nuget/packages/<package>/<version>/analyzers/dotnet/cs`) or a single DLL instead.

## Performance

### Startup Impact
- **Lazy Loading (default)**: No impact on startup time
- **`loadOnStartup: true`**: No impact either: the analyzers load in the background after the project, while the server already answers requests

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
1. Verify file location: `.mcpsharp/autoload-config.json` in the project root (the directory of the solution or project file MCPsharp opened)
2. Check the log (stderr) or the auto-load errors for a file that could not be read, and check JSON syntax validity
3. Check the source names: `project_references` and `builtin_providers`

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
    "Roslyn_Microsoft.CodeQuality.Analyzers.QualityGuidelines.MarkMembersAsStaticAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35",
    "Roslyn_Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines.DoNotDirectlyAwaitATaskAnalyzer@Microsoft.CodeAnalysis.NetAnalyzers+31bf3856ad364e35-10.0.403.0"
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
6. Analysis proceeds normally: each file gets the analyzers that apply to it (see [Which Analyzers Run on a File](#which-analyzers-run-on-a-file))

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
- A project's `.mcpsharp/autoload-config.json` is now read (it was documented before but never read), so check an existing one: replace `nuget_cache` with `project_references`, and use the analyzer IDs `list_roslyn_analyzers` reports in `excludeAnalyzers`
- Analyzer IDs changed from `Roslyn_<type name>` to `Roslyn_<type full name>@<assembly name>[+<culture>][+<public key token>]-<assembly version>`, the culture only when it isn't neutral and the token only for a strong-named assembly, so that analyzers in different assemblies no longer share an ID
- `run_roslyn_analyzers` no longer runs every loaded analyzer on every file: a file gets its own projects' analyzers, plus those loaded with `load_roslyn_analyzers` or from `customAnalyzerPaths`

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
