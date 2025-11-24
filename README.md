# MCPsharp

[![Build and Test](https://github.com/jas88/MCPsharp/actions/workflows/build.yml/badge.svg)](https://github.com/jas88/MCPsharp/actions/workflows/build.yml)

A Model Context Protocol (MCP) server for intelligent C# project analysis and semantic code editing. Provides Claude Code with deep understanding of C# projects including cross-file dependencies, project structure, and Roslyn-based semantic operations.

## Features

- **MCP Protocol Server** — JSON-RPC 2.0 over stdio for Claude Code integration
- **File Operations** — Open projects, list files with glob patterns, read/write/edit files
- **Roslyn Integration** — Direct Roslyn APIs for C# semantic analysis (no subprocess overhead)
- **Semantic Editing** — Find classes, add properties/methods with proper indentation
- **AI-Powered Tools** — Natural language codebase queries using local Ollama or cloud AI (implements [AI-Powered MCP pattern](docs/AI_POWERED_MCP_PATTERN.md))
- **Cross-platform** — macOS, Linux, and Windows support

## Architecture

```
Claude Code
    ↓ MCP (stdio, JSON-RPC)
┌─────────────────────────────────┐
│ MCPsharp Server                 │
├─────────────────────────────────┤
│ • MCP Protocol Handler          │
│ • Project Context Manager       │
│ • File Operations Service       │
│ • Roslyn Workspace (Phase 1)    │
└─────────────────────────────────┘
    ↓ (in-process)
Roslyn APIs
```

See [docs/CONCEPT.md](docs/CONCEPT.md) for full design and [docs/ARCHITECTURE-DECISION.md](docs/ARCHITECTURE-DECISION.md) for architectural choices.

## Build

### Prerequisites

- .NET 9.0 SDK or later
- Git

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Run tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Publish Self-Contained Binaries

```bash
# Linux x64
dotnet publish src/MCPsharp/MCPsharp.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64

# macOS arm64 (Apple Silicon)
dotnet publish src/MCPsharp/MCPsharp.csproj -c Release -r osx-arm64 --self-contained -o publish/osx-arm64

# macOS x64 (Intel)
dotnet publish src/MCPsharp/MCPsharp.csproj -c Release -r osx-x64 --self-contained -o publish/osx-x64

# Windows x64
dotnet publish src/MCPsharp/MCPsharp.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
```

## Installation

### Claude Code Configuration

Add to your Claude Code `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "/path/to/mcpsharp",
      "args": ["/path/to/your/csharp/project"]
    }
  }
}
```

Or using dotnet run for development:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/MCPsharp/src/MCPsharp/MCPsharp.csproj", "/path/to/your/csharp/project"]
    }
  }
}
```

## AI-Powered Tools Setup

MCPsharp includes AI-powered tools that use local or cloud AI to answer natural language questions about your codebase. This implements the [AI-Powered MCP pattern](docs/AI_POWERED_MCP_PATTERN.md) — AI processes verbose data internally and returns concise answers, preventing context pollution.

### Supported AI Providers

MCPsharp auto-detects available AI providers in this order:

1. **Ollama** (Local, free, recommended)
2. **OpenRouter** (Cloud-based, requires API key)
3. None (AI tools disabled)

### Option 1: Ollama (Recommended)

Install and run Ollama locally:

```bash
# macOS
brew install ollama

# Linux
curl -fsSL https://ollama.com/install.sh | sh

# Start Ollama service
ollama serve

# Pull a code-specialized model
ollama pull qwen2.5-coder:3b
```

MCPsharp will automatically detect Ollama at `http://localhost:11434` and use `qwen2.5-coder:3b` by default.

**Environment variables:**
- `OLLAMA_URL` — Ollama server URL (default: `http://localhost:11434`)
- `OLLAMA_MODEL` — Model to use (default: `qwen2.5-coder:3b`)

### Option 2: OpenRouter

Configure OpenRouter API key:

```bash
export OPENROUTER_API_KEY="your-api-key"
```

Or add to your configuration file. MCPsharp will use `anthropic/claude-3.5-sonnet` by default.

### Option 3: Disable AI Tools

Set `AIProvider:Type` to `none` in configuration to disable AI-powered tools.

### Using AI Tools

Once configured, use the `ask_codebase` tool to ask natural language questions:

```json
{
  "name": "ask_codebase",
  "arguments": {
    "question": "How does authentication work in this project?",
    "focus_path": "src/Auth"  // optional
  }
}
```

The AI analyzes your codebase structure, code, and configuration internally and returns a concise answer with file:line references.

### AI Code Transformation Tools

In addition to read-only queries, MCPsharp provides AI-powered code modification tools that use **Roslyn AST transformations** to guarantee correctness:

```json
{
  "name": "ai_suggest_fix",
  "arguments": {
    "file_path": "src/MyService.cs",
    "description": "Fix the null reference exception in ProcessData",
    "line_number": 42,
    "apply_changes": false  // Preview first (default)
  }
}
```

**Why Roslyn AST instead of text manipulation?**

Traditional tools use sed scripts, regex, or Python text manipulation which can:
- Generate syntactically invalid code
- Break compilation
- Corrupt code structure
- Introduce subtle bugs

MCPsharp's AI transformation tools use Roslyn's Abstract Syntax Tree (AST) to:
- ✅ Guarantee syntactically valid C# code
- ✅ Preserve compilation integrity
- ✅ Maintain code structure and formatting
- ✅ Validate changes before returning them
- ✅ Use semantic awareness for refactoring

**Available transformation tools:**
- `ai_suggest_fix` - Bug fixes with Roslyn validation
- `ai_refactor` - Semantic-aware refactoring (preserves behavior)
- `ai_implement_feature` - AST-based code generation

All tools return a **preview by default**. Set `apply_changes: true` to modify files directly.

## Usage

### Running the Server

```bash
# Run on current directory (auto-loads .sln/.csproj if found)
dotnet run --project src/MCPsharp/MCPsharp.csproj

# Run on specific project
dotnet run --project src/MCPsharp/MCPsharp.csproj /path/to/project

# Or use published binary
./mcpsharp /path/to/project
```

**Auto-Loading**: If MCPsharp is launched in a directory containing `.sln` or `.csproj` files, it will automatically load the project on startup. Solution files (`.sln`) are preferred over project files when both exist.

### Testing the Server

```bash
# Test initialize
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | dotnet run --project src/MCPsharp/MCPsharp.csproj

# Test tools/list
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | dotnet run --project src/MCPsharp/MCPsharp.csproj

# Run manual test script
./tests/manual-test.sh
```

### Available Tools

The server provides 41 MCP tools:

**File Operations:**
- `project_open` - Open a project directory
- `project_info` - Get current project information
- `file_list` - List files with optional glob patterns
- `file_read` - Read file contents
- `file_write` - Write to a file
- `file_edit` - Apply text edits to a file

**Roslyn/Semantic Analysis:**
- `find_symbol` - Find symbols by name
- `get_symbol_info` - Get detailed symbol information
- `get_class_structure` - Get complete class structure
- `add_class_property` - Add a property to a class
- `add_class_method` - Add a method to a class
- `find_references` - Find all symbol references
- `find_implementations` - Find interface implementations
- `parse_project` - Parse .csproj files

**Workflow & Configuration:**
- `get_workflows` - Get GitHub Actions workflows
- `parse_workflow` - Parse workflow YAML
- `validate_workflow_consistency` - Validate workflow vs project
- `get_config_schema` - Get config file schema
- `merge_configs` - Merge configuration files

**Advanced Analysis (Phase 2):**
- `analyze_impact` - Analyze code change impact
- `trace_feature` - Trace features across files
- `rename_symbol` - Rename symbols across the codebase
- `find_callers` - Find all callers of a method
- `find_call_chains` - Analyze method call chains
- `find_type_usages` - Find all usages of a type
- `analyze_call_patterns` - Analyze method call patterns
- `analyze_inheritance` - Analyze type inheritance
- `find_circular_dependencies` - Find circular dependencies
- `find_unused_methods` - Find unused methods
- `analyze_call_graph` - Analyze call graphs
- `find_recursive_calls` - Find recursive call chains
- `analyze_type_dependencies` - Analyze type dependencies

**Code Quality:**
- `code_quality_analyze` - Analyze code quality issues
- `code_quality_fix` - Apply automated fixes
- `code_quality_profiles` - List available fix profiles
- `extract_method` - Extract code to new method

**AI-Powered Tools (Read-Only):**
- `ask_codebase` - Ask natural language questions about the codebase using AI (returns concise answers, not verbose data)

**AI-Powered Code Transformation (Roslyn AST-based):**
- `ai_suggest_fix` - AI suggests bug fixes using Roslyn transformations (guarantees syntactically valid C#)
- `ai_refactor` - AI-guided refactoring with semantic awareness (preserves program behavior)
- `ai_implement_feature` - AI implements features using AST-based code generation (no string templates)

**IMPORTANT:** The AI transformation tools use Roslyn AST instead of text manipulation (sed/regex/Python) to ensure all code changes are syntactically valid and preserve compilation integrity. Always prefer these tools for C# code modifications.

## Development

### Project Structure

```
MCPsharp/
├── src/
│   └── MCPsharp/           # Main MCP server
├── tests/
│   └── MCPsharp.Tests/     # xUnit tests
├── docs/                   # Documentation
│   ├── CONCEPT.md          # Design document
│   ├── MVP-PLAN.md         # Implementation plan
│   └── ARCHITECTURE-DECISION.md
├── .github/
│   └── workflows/
│       └── build.yml       # CI/CD workflow
└── MCPsharp.sln            # Solution file
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --verbosity detailed

# Run specific test project
dotnet test tests/MCPsharp.Tests/MCPsharp.Tests.csproj

# Watch mode (re-run on file changes)
dotnet watch test
```

### Debugging

```bash
# Run with debug output
dotnet run --project src/MCPsharp/MCPsharp.csproj --configuration Debug

# Attach debugger in VS Code or Rider
```

## MCP Tools (Planned)

### Phase 0: Basic File Operations
- `project_open` — Open a C# project directory
- `project_info` — Get project metadata
- `file_list` — List files with glob patterns
- `file_read` — Read file contents
- `file_write` — Write file contents
- `file_edit` — Apply text edits

### Phase 1: Semantic C# Operations
- `language_server_start` — Initialize Roslyn workspace
- `find_symbol` — Find classes, methods, properties by name
- `get_symbol_info` — Get detailed symbol information
- `get_class_structure` — Get class members and structure
- `add_class_property` — Add property to class with correct indentation
- `add_class_method` — Add method to class
- `find_references` — Find all references to a symbol

See [docs/MVP-PLAN.md](docs/MVP-PLAN.md) for detailed tool specifications.

## Dependencies

### Runtime Dependencies
- **Microsoft.CodeAnalysis.CSharp** (4.11.0) — Roslyn C# compiler APIs
- **Microsoft.CodeAnalysis.CSharp.Workspaces** (4.11.0) — Roslyn workspace APIs
- **Microsoft.Extensions.FileSystemGlobbing** (9.0.0) — Glob pattern matching
- **Microsoft.Extensions.Logging** (9.0.0) — Logging infrastructure

### Test Dependencies
- **xUnit** (2.9.2) — Test framework
- **FluentAssertions** (6.12.1) — Fluent assertion library
- **Moq** (4.20.72) — Mocking framework
- **coverlet.collector** (6.0.2) — Code coverage

## CI/CD

GitHub Actions workflow runs on every push and PR:
- Builds on Linux, macOS, and Windows
- Runs all tests with code coverage
- Publishes self-contained binaries on main branch
- Uploads coverage to Codecov

## Roadmap

- ✅ **Phase 0** (Current): Basic MCP server with file operations
- 🚧 **Phase 1** (Next): Roslyn integration for semantic C# operations
- 📋 **Phase 2** (Future): Project/solution parsing, multi-project support
- 📋 **Phase 3** (Future): Configuration analysis, workflow validation
- 📋 **Phase 4** (Future): Database migrations, refactoring tools

## Contributing

This is a personal project by @jas88. Contributions are welcome!

1. Fork the repository
2. Create a feature branch
3. Make your changes with tests
4. Submit a pull request

## License

[To be determined]

## Documentation

- [CONCEPT.md](docs/CONCEPT.md) — Full design specification
- [MVP-PLAN.md](docs/MVP-PLAN.md) — Implementation plan and timeline
- [ARCHITECTURE-DECISION.md](docs/ARCHITECTURE-DECISION.md) — Why direct Roslyn vs subprocess
- [CLAUDE.md](CLAUDE.md) — Instructions for Claude Code instances

## Support

- Issues: https://github.com/jas88/MCPsharp/issues
- Discussions: https://github.com/jas88/MCPsharp/discussions
