# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

MCPsharp is a Model Context Protocol (MCP) server for intelligent C# project analysis and semantic code editing. It provides Claude Code with deep understanding of C# projects including cross-file dependencies, workflow configuration, project structure, and polyglot file analysis.

**Key Architecture:** Single self-contained binary wrapping `csharp-ls`, using progressive analysis model - returns "project loaded" quickly while continuing deep analysis in background, similar to Visual Studio's loading experience.

## Project Goals

1. **Semantic code editing** — AST-aware edits instead of brittle text transformations
2. **Project-wide context** — Complete codebase map before editing
3. **Safe large-file edits** — Understand structure and dependencies to avoid breaking changes
4. **Monorepo support** — Handle complex multi-solution project structures
5. **Polyglot awareness** — Understand .NET projects holistically: C#, XML (.csproj/.sln), JSON/YAML config, SQL migrations, GitHub workflows, Markdown docs
6. **Self-contained deployment** — Single binary, no external tool installation required

## Build Commands

```bash
# Build the project
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Run tests
dotnet test

# Create self-contained binary
dotnet publish -c Release -r osx-arm64 --self-contained

# Create cross-platform binaries
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r win-x64 --self-contained
```

## Target Environment

- **Platform:** macOS, Linux (Windows support planned)
- **Runtime:** .NET 9 SDK
- **Embedded dependency:** csharp-ls (bundled, not user-installed)
- **Project types:** Modern C# (.NET 6+), including monorepos with multiple solutions
- **File sizes:** Handles large files (1000+ LOC) and source-generated code

## Architecture

### Component Hierarchy

```
Claude Code
    ↓ MCP (stdio, JSON-RPC)
┌─────────────────────────────────┐
│ C# Project MCP Server           │
├─────────────────────────────────┤
│ • MCP Protocol Handler          │
│ • Background Analysis Manager   │
│ • Multi-language Analyzer       │
│ • Project Index & Cache         │
└─────────────────────────────────┘
    ↓ LSP (stdio)
┌─────────────────────────────────┐
│ csharp-ls (embedded)            │
│ ↓ Roslyn                        │
│ ↓ MSBuild                       │
└─────────────────────────────────┘
```

### Key Components

**1. MCP Protocol Handler**
- Manages stdin/stdout communication with Claude Code
- Implements JSON-RPC 2.0 for MCP spec compliance
- Routes tool calls to appropriate analyzers

**2. Background Analysis Manager**
- State machine: `Idle → Initializing → Loaded → FullyAnalyzed`
- Coordinates incremental re-analysis on file changes
- Maintains Roslyn compilation state
- Provides non-blocking analysis that fills in detail over time

**3. Multi-Language Analyzer**
- **CSharpAnalyzer** — Wraps csharp-ls, provides semantic queries
- **ProjectAnalyzer** — Parses .csproj/.sln files, builds dependency graph
- **ConfigAnalyzer** — Parses JSON/YAML config files
- **WorkflowAnalyzer** — Parses GitHub Actions workflows
- **SqlAnalyzer** — Parses Entity Framework migrations
- **DocumentationAnalyzer** — Extracts Markdown structure

**4. Project Index & Cache**
- In-memory index of symbols, types, files, configurations
- Incremental update on file changes
- Optional disk persistence for faster startup

## MCP Tools (Planned)

The server will provide these tools to Claude Code:

### Project Structure
- `get_project_overview()` — Immediate snapshot of project shape
- `get_project_references()` — Project-level dependency graph

### File Analysis
- `summarize_file(path)` — Structured overview of a single file
- `get_type_hierarchy(namespace?)` — Classes, interfaces, records in namespace

### Semantic Analysis
- `find_references(symbol)` — Find all uses of a symbol
- `get_definition(symbol)` — Resolve symbol to definition
- `get_dependents(symbol)` — What depends on this?
- `get_full_impact(change)` — Predict impact across all file types
- `get_call_chain(method, direction)` — Backward/forward call analysis

### Project Mapping
- `trace_feature(featureName)` — Find all code implementing a feature
- `get_architecture()` — Layer structure and violations

### Configuration & Environment
- `get_configuration_schema()` — Config file locations and structure
- `get_workflows()` — GitHub Actions workflow definitions
- `get_workflow_impact(change)` — Check if code change breaks CI/CD

### Code Quality
- `detect_issues()` — Quick scan for code smells
- `validate_consistency()` — Check .csproj vs. workflow vs. code

### Database & Migrations
- `analyze_migrations()` — Entity Framework migration history

### Code Editing
- `edit_file(path, edits)` — Apply semantic-aware edits
- `get_call_chain(method, direction)` — Method call analysis

## Implementation Phases

### Phase 1: MVP (Current)
- MCP protocol handler
- Basic background analysis state machine
- csharp-ls LSP client wrapper
- Project/solution parsing (.csproj, .sln)
- Core tools: `get_project_overview`, `summarize_file`, `find_references`, `edit_file`

### Phase 2: Polyglot & Cross-File Impact
- YAML workflow parsing
- JSON/YAML config analysis
- `get_full_impact()` cross-file change prediction
- `trace_feature()` multi-file navigation

### Phase 3: Quality & Depth
- SQL migration analysis
- Architecture layer validation
- Duplicate code detection
- Large-file optimization

### Phase 4: Polish & Extensibility
- Incremental re-analysis on file changes
- Caching layer
- Custom analyzer extensibility
- Documentation

## Technical Decisions

**Embedded csharp-ls**
- Single binary deployment (no separate tool install)
- Version control (controlled upgrades)
- Managed lifecycle integration
- Trade-off: Larger binary (~150-200MB)

**Background Analysis**
- Progressive UX (returns quick partial results)
- State machine for incremental updates
- Feels fast even on large projects

**Roslyn + csharp-ls Combination**
- Roslyn: Deep AST analysis for large files, source generators
- csharp-ls: Proven LSP implementation, clean stdio interface
- Best of both worlds

**Native .NET Parsers**
- YamlDotNet for YAML (workflows)
- Built-in System.Xml, System.Text.Json
- Tight integration with C# analysis

## Testing Strategy

### Unit Tests
- Analyzer implementations (YAML, JSON, project parsing)
- State machine transitions
- Edit application logic

### Integration Tests
- Real C# projects (small, medium, large)
- Monorepo scenarios
- Source generator handling
- Cross-file impact predictions

### Performance Benchmarks
- Project load time (target: <5s for 500-file project)
- Analysis completion time
- Tool response latency (<1s for most queries)

## Installation (Planned)

```bash
# Claude Code configuration (claude_desktop_config.json)
"mcpServers": {
  "csharp": {
    "command": "/path/to/csharp-mcp",
    "args": ["--workspace", "/path/to/project"]
  }
}
```

## Key Dependencies

**NuGet packages:**
- `OmniSharp.Extensions.LanguageServer` — MCP/LSP protocol implementation
- `YamlDotNet` — YAML parsing for workflows
- `System.Xml`, `System.Text.Json` — XML/JSON parsing (built-in)

**Embedded binary:**
- csharp-ls — Packaged as .NET tool within published binary

## Success Criteria

- Claude Code can edit large C# files safely using AST, not text patterns
- Single binary deployment, no external tool installation
- Project loads within 5 seconds, full analysis within 30 seconds
- Monorepo support (multiple solutions, cross-project dependencies)
- Handles source generators transparently
- AI gets project-wide context (workflows, config, architecture) before editing
- `get_full_impact()` accurately predicts cross-file changes
- macOS + Linux primary support
