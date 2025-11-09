# Analyzer Auto-Loading

MCPsharp automatically discovers and loads Roslyn analyzers without requiring explicit initialization. This eliminates the need for users to manually call `load_roslyn_analyzers` before using analysis features.

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
2. **Smart Discovery**: Scans standard locations for installed analyzer packages
3. **Automatic Registration**: Registers discovered analyzers with the analyzer host
4. **One-Time Operation**: Auto-load runs once and caches results
5. **Non-Blocking**: Failures in auto-load are logged but don't break MCPsharp

## Analyzer Sources

### 1. NuGet Package Cache
Auto-discovers analyzers from `~/.nuget/packages/`, including:

- **Microsoft.CodeAnalysis.NetAnalyzers** - Official .NET analyzers
- **Roslynator.Analyzers** - Roslynator code quality analyzers
- **SonarAnalyzer.CSharp** - SonarQube C# analyzers
- **ErrorProne.NET.CodeFixes** - ErrorProne.NET analyzers
- **ErrorProne.NET.Structs** - Struct-specific analyzers
- **AsyncFixer** - Async/await pattern analyzers
- **Meziantou.Analyzer** - Meziantou's code quality analyzers

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
  "analyzerSources": ["nuget_cache", "builtin_providers"],
  "excludeAnalyzers": [],
  "customAnalyzerPaths": []
}
```

### Custom Configuration
Create `.mcpsharp/autoload-config.json` in your project root:

```json
{
  "autoLoadEnabled": true,
  "loadOnStartup": false,
  "analyzerSources": [
    "nuget_cache",
    "builtin_providers"
  ],
  "excludeAnalyzers": [
    "Roslynator.RCS1001",
    "SonarAnalyzer.S1234"
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

#### `analyzerSources` (array of strings)
Sources to scan for analyzers:
- `"nuget_cache"` - Scan NuGet package cache
- `"builtin_providers"` - Register built-in code fix providers

#### `excludeAnalyzers` (array of strings)
Analyzer IDs to exclude from auto-loading. Useful for disabling specific analyzers you don't want.

#### `customAnalyzerPaths` (array of strings)
Custom directories or DLL files to load analyzers from. Supports:
- Directory paths: Scans recursively for analyzer DLLs
- File paths: Loads specific analyzer DLL

## Performance

### Startup Impact
- **Lazy Loading (default)**: No impact on startup time
- **Eager Loading**: Adds ~1-2 seconds for typical setups with 3-5 analyzer packages

### First Use Latency
- **Without auto-load**: Requires manual load call + analysis time
- **With auto-load**: Auto-load (1-2s) + analysis time on first request
- **Subsequent requests**: No auto-load overhead (cached)

### Recommendations
- Use default lazy loading for best startup performance
- Enable `loadOnStartup: true` only if you need immediate analyzer availability

## Manual Loading Still Available

Auto-loading doesn't replace manual loading - you can still use `load_roslyn_analyzers` to:
- Load custom analyzers not in standard locations
- Reload analyzers after updating packages
- Load analyzers from specific paths

## Troubleshooting

### Analyzers Not Loading
1. Check logs for auto-load errors (stderr)
2. Verify NuGet packages are installed: `ls ~/.nuget/packages/`
3. Try manual load: call `load_roslyn_analyzers` with explicit path
4. Check configuration: ensure `autoLoadEnabled: true`

### Performance Issues
1. Disable eager loading: set `loadOnStartup: false`
2. Exclude large analyzer packages via `excludeAnalyzers`
3. Disable auto-load and use manual loading for specific analyzers

### Configuration Not Applied
1. Verify file location: `.mcpsharp/autoload-config.json` in project root
2. Check JSON syntax validity
3. Restart MCPsharp after configuration changes

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
    "Microsoft.CodeAnalysis.CSharp.NetAnalyzers.CA1014",
    "Roslynator.RCS1001"
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
1. User calls `run_roslyn_analyzers` (or any analyzer tool)
2. `RoslynAnalyzerService.RunAnalyzersAsync()` checks if analyzers loaded
3. If not loaded, calls `AnalyzerAutoLoadService.EnsureAnalyzersLoadedAsync()`
4. Auto-load service discovers and loads analyzers from configured sources
5. Analyzers registered with `AnalyzerRegistry`
6. Analysis proceeds normally

### Thread Safety
- Auto-load is thread-safe (uses locking)
- Multiple concurrent requests trigger only one auto-load operation
- Subsequent requests wait for auto-load completion

### Error Handling
- Individual analyzer load failures are logged but don't fail auto-load
- NuGet package scan failures are logged as warnings
- Auto-load failure doesn't crash MCPsharp - falls back to empty analyzer list

## Migration Guide

### For Existing Users
No action required. Existing workflows continue to work:
- Manual `load_roslyn_analyzers` calls still work
- No breaking changes to API
- Auto-loading is additive functionality

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
- **Package manager integration**: Auto-detect new analyzer packages installed via NuGet
- **Smart defaults**: Machine learning to predict which analyzers you'll use
