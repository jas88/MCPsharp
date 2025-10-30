# MCPsharp Analyzer Integration System

This document describes the complete code analyzer integration system implemented in MCPsharp, providing semantic code analysis, fix generation, and automated refactoring capabilities.

## Overview

The analyzer integration system enables MCPsharp to load, manage, and execute code analyzers that can detect issues in C# code and suggest or automatically apply fixes. It provides a secure, sandboxed environment for running third-party analyzers with comprehensive permission management.

## Architecture

### Core Components

1. **Models** (`/src/MCPsharp/Models/Analyzers/`)
   - `IAnalyzer.cs` - Interface for implementing analyzers
   - `AnalyzerModels.cs` - Core data models for issues, fixes, and results
   - `ISecurityManager.cs` - Security validation and access control
   - `IAnalyzerSandbox.cs` - Isolated execution environment
   - `IAnalyzerHost.cs` - Analyzer lifecycle management
   - `IAnalyzerRegistry.cs` - Analyzer discovery and registration
   - `IFixEngine.cs` - Fix application with conflict resolution
   - `AnalyzerSchemas.cs` - JSON schemas for MCP tools

2. **Services** (`/src/MCPsharp/Services/Analyzers/`)
   - `SecurityManager.cs` - Security validation, signature checking, permissions
   - `AnalyzerSandbox.cs` - Isolated execution with resource limits
   - `AnalyzerRegistry.cs` - Analyzer discovery and management
   - `AnalyzerHost.cs` - Central coordinator for analyzer operations
   - `AnalyzerConfigurationManager.cs` - Configuration management system
   - `AnalyzerTools.cs` - MCP tool implementations

3. **Built-in Analyzers** (`/src/MCPsharp/Services/Analyzers/BuiltIn/`)
   - `NUnitUpgraderAnalyzer.cs` - NUnit v2/v3 to v4 migration analyzer

4. **Fix Engine** (`/src/MCPsharp/Services/Analyzers/Fixes/`)
   - `FixEngine.cs` - Comprehensive fix application system

## Key Features

### Security Framework

- **Assembly Validation**: Digital signature verification, malicious pattern detection
- **Permission System**: Granular access control for file operations, network access, command execution
- **Sandbox Execution**: Resource-limited isolated environment for analyzer execution
- **Audit Logging**: Complete security event tracking for compliance

### Analyzer Management

- **Dynamic Loading**: Runtime loading of analyzer assemblies with compatibility checking
- **Registry System**: Central registry for analyzer discovery and management
- **Configuration System**: Hierarchical configuration (global → project → analyzer-specific)
- **Health Monitoring**: Real-time health status and performance metrics

### Fix Engine

- **Conflict Detection**: Automatic detection of overlapping or conflicting fixes
- **Conflict Resolution**: Multiple strategies for resolving fix conflicts
- **Preview System**: Preview changes before applying them
- **Rollback Capability**: Full rollback with backup management
- **Batch Processing**: Efficient application of multiple fixes

### MCP Integration

The system provides 10 MCP tools for analyzer operations:

1. **analyzer_list** - List available analyzers with filtering
2. **analyzer_run** - Run analysis on specified files
3. **analyzer_get_fixes** - Get available fixes for issues
4. **analyzer_apply_fixes** - Apply fixes with conflict resolution
5. **analyzer_load** - Load analyzer from assembly
6. **analyzer_unload** - Unload analyzer and clean up
7. **analyzer_configure** - Configure analyzer settings
8. **analyzer_get_health** - Get analyzer health status
9. **analyzer_get_fix_history** - Get fix history and statistics
10. **analyzer_rollback_fixes** - Rollback previously applied fixes

## Built-in NUnit Upgrader Analyzer

The system includes a comprehensive NUnit migration analyzer that handles 6 common migration patterns:

### Supported Rules

1. **NUNIT001** - Remove `[TestFixture]` attribute (not needed in v4)
2. **NUNIT002** - Update `Assert.AreEqual` parameter order (expected, actual)
3. **NUNIT003** - Verify `[SetUp]` attribute usage
4. **NUNIT004** - Replace `[ExpectedException]` with `Assert.Throws`
5. **NUNIT005** - Replace `string.IsNullOrEmpty` checks with `Assert.That`
6. **NUNIT006** - Update using statements for NUnit v4 namespaces

### Example Usage

```json
{
  "tool": "analyzer_run",
  "arguments": {
    "analyzer_id": "NUnitUpgrader",
    "files": ["tests/**/*.cs"],
    "generate_fixes": true
  }
}
```

## Security Model

### Assembly Validation

- **Digital Signature Verification**: Validates assembly signatures against trusted certificates
- **Malicious Pattern Detection**: Scans for suspicious code patterns
- **Checksum Verification**: Ensures assembly integrity

### Permission System

Analyzers are granted specific permissions:

```csharp
public class AnalyzerPermissions
{
    public bool CanReadFiles { get; init; } = true;
    public bool CanWriteFiles { get; init; } = false;
    public bool CanExecuteCommands { get; init; } = false;
    public bool CanAccessNetwork { get; init; } = false;
    public ImmutableArray<string> AllowedPaths { get; init; }
    public ImmutableArray<string> DeniedPaths { get; init; }
}
```

### Sandbox Limits

- **Memory Usage**: Configurable memory limits (default: 512MB)
- **Execution Time**: Time limits for analysis (default: 5 minutes)
- **File Access**: Configurable file access limits
- **Network Access**: Disabled by default for security

## Configuration System

### Global Configuration

```json
{
  "NUnitUpgrader": {
    "is_enabled": true,
    "rules": {
      "NUNIT001": {
        "is_enabled": true,
        "severity": "Info"
      }
    },
    "include_files": ["tests/**/*.cs"],
    "exclude_files": ["**/obj/**"]
  }
}
```

### Project-Specific Configuration

Project-level configurations override global settings:

```json
{
  "project_path": "/path/to/project",
  "NUnitUpgrader": {
    "rules": {
      "NUNIT002": {
        "severity": "Error"
      }
    }
  }
}
```

## Usage Examples

### Loading an External Analyzer

```json
{
  "tool": "analyzer_load",
  "arguments": {
    "assembly_path": "/path/to/analyzer.dll",
    "auto_enable": true,
    "permissions": {
      "can_read_files": true,
      "can_write_files": false,
      "allowed_paths": ["/path/to/project"]
    }
  }
}
```

### Running Analysis

```json
{
  "tool": "analyzer_run",
  "arguments": {
    "analyzer_id": "CustomAnalyzer",
    "files": ["src/**/*.cs"],
    "rules": ["RULE001", "RULE002"],
    "configuration": {
      "severity_level": "Warning"
    },
    "generate_fixes": true
  }
}
```

### Applying Fixes

```json
{
  "tool": "analyzer_apply_fixes",
  "arguments": {
    "analyzer_id": "NUnitUpgrader",
    "issue_ids": ["issue1", "issue2"],
    "preview_only": false,
    "resolve_conflicts": true,
    "conflict_strategy": "PreferNewer",
    "create_backup": true
  }
}
```

## Implementation Details

### Creating Custom Analyzers

Implement the `IAnalyzer` interface:

```csharp
public class MyAnalyzer : IAnalyzer
{
    public string Id => "MyAnalyzer";
    public string Name => "My Custom Analyzer";
    public string Description => "Description of what it does";
    public Version Version => new(1, 0, 0);
    public string Author => "Your Name";
    public ImmutableArray<string> SupportedExtensions => ImmutableArray.Create(".cs");

    public async Task<AnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        // Implementation here
    }

    public ImmutableArray<AnalyzerRule> GetRules()
    {
        // Define analyzer rules
    }

    public ImmutableArray<AnalyzerFix> GetFixes(string ruleId)
    {
        // Generate fixes for rules
    }
}
```

### Fix Generation

Fixes are generated with confidence levels and can be:

- **Non-interactive**: Applied automatically (high confidence)
- **Interactive**: Require user input (medium/low confidence)
- **Batchable**: Can be applied with other fixes
- **Standalone**: Must be applied individually

### Error Handling

The system provides comprehensive error handling:

- **Security Violations**: Blocked operations with audit logging
- **Compatibility Issues**: Version and dependency checking
- **Resource Limits**: Graceful handling of memory/time limits
- **Fix Conflicts**: Automatic resolution with user override options

## Performance Considerations

- **Background Analysis**: Non-blocking analysis with progressive results
- **Caching**: In-memory caching of analysis results
- **Incremental Updates**: Only re-analyze changed files
- **Resource Management**: Automatic cleanup of unused analyzers
- **Parallel Processing**: Concurrent analysis of multiple files

## Monitoring and Observability

### Health Metrics

- Analyzer uptime and availability
- Analysis success/failure rates
- Performance metrics (execution time, memory usage)
- Security event tracking

### Audit Logging

All analyzer actions are logged for compliance:

- Assembly loading/unloading
- Security validation results
- Permission changes
- Fix application and rollback
- Configuration modifications

## Future Enhancements

Planned improvements include:

- **Additional Built-in Analyzers**: Code quality, performance, security analyzers
- **Marketplace Integration**: Discover and install community analyzers
- **AI-Powered Fixes**: Machine learning for fix generation
- **Advanced Conflict Resolution**: Smarter conflict resolution algorithms
- **Cross-Language Support**: Support for other .NET languages
- **IDE Integration**: Visual Studio and VS Code extensions

## Conclusion

The analyzer integration system provides a robust, secure, and extensible platform for code analysis and automated refactoring. It combines powerful analysis capabilities with comprehensive security measures and user-friendly tools, making it an essential component of the MCPsharp ecosystem.

The system is designed to be both developer-friendly and enterprise-ready, with features like audit logging, permission management, and rollback capabilities that make it suitable for use in production environments.