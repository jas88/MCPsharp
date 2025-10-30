# C# Project MCP Server

## Overview

A Language Model Context Protocol (MCP) server for intelligent C# project analysis and semantic code editing. Designed as a single self-contained binary that wraps `csharp-ls`, providing Claude Code with deep understanding of C# projects including cross-file dependencies, workflow configuration, project structure, and polyglot file analysis.

**Key differentiator:** Progressive analysis model. Returns "project loaded" quickly while continuing deep analysis in the background, similar to Visual Studio's loading experience.

## Goals

1. **Enable semantic code editing** — Replace brittle sed/awk/Python text transformations with AST-aware edits
2. **Project-wide context** — Give Claude Code a complete map of the codebase before editing
3. **Safe large-file edits** — Understand structure and dependencies to avoid breaking changes
4. **Monorepo support** — Handle complex multi-solution project structures
5. **Polyglot awareness** — Understand .NET projects holistically: C#, XML (.csproj/.sln), JSON/YAML config, SQL migrations, GitHub workflows, Markdown docs
6. **Self-contained deployment** — Single binary, no external tool installation required

## Target Environment

- **Primary client:** Claude Code
- **Platform support:** macOS, Linux (and Windows, but not required for MVP)
- **Runtime:** .NET 9 SDK
- **Embedded dependency:** csharp-ls (bundled, not user-installed)
- **Project types:** Modern C# (.NET 6+), including monorepos with multiple solutions
- **File sizes:** Handles large files (1000+ LOC) and source-generated code

## Architecture

### High Level

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

### Component Design

#### 1. MCP Protocol Handler
- Manages stdin/stdout communication with Claude Code
- Implements JSON-RPC 2.0 for MCP spec compliance
- Routes tool calls to appropriate analyzers
- Handles connection lifecycle and error reporting

**Key responsibility:** Translate MCP tool calls into internal analyzer calls.

#### 2. Background Analysis Manager
- Maintains project analysis state machine: `Idle → Initializing → Loaded → FullyAnalyzed`
- Coordinates incremental re-analysis on file changes
- Manages Roslyn compilation state for the project
- Provides progress/status to callers

**Key responsibility:** Non-blocking analysis that fills in detail over time.

**State transitions:**
```
Idle
  ↓ project_open() called
Initializing (parse .sln, enumerate files, start csharp-ls)
  ↓ (3-5s typically)
Loaded (basic structure available, queries return partial results)
  ↓ (background parsing continues)
FullyAnalyzed (full symbol analysis complete)
  ↓ file changes detected
Loaded (revert to partial mode, re-analyze)
```

**Staleness rules:**
- `get_project_overview()` — always returns current (even if Initializing)
- `get_type_hierarchy()` — returns partial if Loaded, full if FullyAnalyzed
- `find_references()` — blocks until FullyAnalyzed (critical accuracy)
- `detect_issues()` — returns current state (okay to be incomplete)

#### 3. Multi-Language Analyzer
Modular analyzers for each file type:

**CSharpAnalyzer**
- Wraps csharp-ls LSP client
- Provides semantic queries (definition, references, diagnostics)
- Handles source-generated code visibility
- Caches Roslyn CompilationUnit per file

**ProjectAnalyzer**
- Parses `.csproj`, `.sln` files (XML)
- Extracts: target frameworks, package references, project dependencies, properties
- Builds project graph (which project references which)

**ConfigAnalyzer**
- Parses JSON config files (appsettings.*.json)
- Parses YAML (github workflows, docker-compose)
- Merges environment-specific configs
- Tracks secrets/sensitive keys

**WorkflowAnalyzer**
- Parses `.github/workflows/*.yml`
- Extracts: triggers, environment, jobs, steps, secrets, matrix strategies
- Correlates with project configuration (e.g., dotnet-version in workflow vs. TargetFramework in .csproj)

**SqlAnalyzer**
- Parses Entity Framework migration files
- Extracts table/column definitions from Up() methods
- Tracks schema evolution
- Links to C# DbContext definitions

**DocumentationAnalyzer**
- Extracts Markdown structure (headings, sections)
- Identifies "out of sync" sections (mentions old code patterns)
- Links sections to relevant code files

#### 4. Project Index & Cache
- In-memory index of all symbols, types, files, configurations
- Incremental update on file changes (watch filesystem)
- Exports as queryable data structures
- Persists lightweight cache to disk (optional, for faster startup on re-open)

**What's indexed:**
- File→Type mappings (which file defines which types)
- Type→References (where each type/method is used)
- Dependencies (which projects/packages depend on which)
- Configuration keys and their values
- Workflow → project configuration mappings

### External Dependencies

**NuGet packages:**
- `OmniSharp.Extensions.LanguageServer` — MCP/LSP protocol implementation
- `YamlDotNet` — YAML parsing (workflows)
- `System.Xml`, `System.Text.Json` — XML/JSON parsing (built-in)

**Embedded binary:**
- csharp-ls — packaged as a .NET tool within the published binary

**Runtime:**
- .NET 9 runtime (included in self-contained deployment)

## MCP Tools

### Project Structure

#### `get_project_overview()`
Returns immediate snapshot of project shape.

**Response:**
```json
{
  "workspace": "/path/to/workspace",
  "solutionFiles": ["MyApp.sln"],
  "projects": [
    {
      "name": "MyApp.Core",
      "path": "src/Core/Core.csproj",
      "type": "library",
      "targetFrameworks": ["net9.0"]
    }
  ],
  "rootNamespaces": ["MyApp", "MyApp.Core"],
  "analysisState": "Loaded",
  "analysisProgress": 0.65,
  "estimatedTimeRemaining": "2.3s"
}
```

**Returns immediately.** Progress indicates background analysis completion.

---

#### `get_project_references()`
Project-level dependency graph.

**Response:**
```json
{
  "projectDependencies": [
    {
      "name": "MyApp.Core",
      "path": "src/Core/Core.csproj",
      "dependsOn": ["MyApp.Models"]
    }
  ],
  "packageReferences": [
    {
      "id": "Microsoft.EntityFrameworkCore",
      "version": "8.0.0",
      "projects": ["MyApp.Core", "MyApp.Data"]
    }
  ]
}
```

**Returns immediately** (from index).

---

### File Analysis

#### `summarize_file(path: string)`
Structured overview of a single file.

**Response:**
```json
{
  "path": "src/Core/Services/UserService.cs",
  "fileSize": 245,
  "types": [
    {
      "name": "UserService",
      "kind": "class",
      "implements": ["IUserService"],
      "baseClass": null,
      "publicMethods": [
        { "name": "GetUser", "signature": "GetUser(Guid id): Task<User>" },
        { "name": "CreateUser", "signature": "CreateUser(CreateUserDto dto): Task<User>" }
      ],
      "dependencies": ["IUserRepository", "ILogger<UserService>"],
      "loc": 120,
      "complexity": "medium"
    }
  ],
  "usings": ["System", "System.Threading.Tasks"],
  "fileAnalysis": {
    "hasTests": false,
    "hasComments": true,
    "commentDensity": 0.12
  },
  "analysisState": "Complete"
}
```

**Blocks if FullyAnalyzed.** Returns partial if Loaded.

---

#### `get_type_hierarchy(namespace?: string)`
Classes, interfaces, records in a namespace.

**Response:**
```json
{
  "namespace": "MyApp.Services",
  "types": [
    {
      "name": "UserService",
      "kind": "class",
      "methods": 8,
      "loc": 120
    },
    {
      "name": "IUserService",
      "kind": "interface",
      "methods": 5
    }
  ],
  "analysisState": "Complete"
}
```

---

### Semantic Analysis

#### `find_references(symbol: string, file?: string, line?: number, column?: number)`
Find all uses of a symbol.

**Response:**
```json
{
  "symbol": "UserService.GetUser",
  "references": [
    {
      "file": "src/Controllers/UserController.cs",
      "line": 45,
      "usageCount": 3
    },
    {
      "file": "tests/UserServiceTests.cs",
      "line": 120,
      "usageCount": 12
    }
  ],
  "totalReferences": 15
}
```

**Blocks until FullyAnalyzed** (accuracy critical).

---

#### `get_definition(symbol: string)`
Resolve symbol to its definition.

**Response:**
```json
{
  "symbol": "IUserService",
  "file": "src/Core/Services/IUserService.cs",
  "line": 5,
  "column": 18,
  "fullSource": "public interface IUserService { ... }"
}
```

**Delegates to csharp-ls.**

---

#### `get_dependents(symbol: string)`
What depends on this?

**Response:**
```json
{
  "symbol": "UserService",
  "directDependents": [
    { "file": "Controllers/UserController.cs", "usageCount": 3 },
    { "file": "Tests/UserServiceTests.cs", "usageCount": 12 }
  ],
  "transitiveImpact": [
    { "file": "Middleware/AuthMiddleware.cs", "reason": "via IUserService" }
  ]
}
```

---

#### `get_full_impact(change: EditChange)`
Predict impact of a code change across all file types.

**Request:**
```json
{
  "file": "src/Core/Models/User.cs",
  "type": "User",
  "method": "Delete",
  "changeType": "signature_change"
}
```

**Response:**
```json
{
  "csharpImpact": [
    { "file": "Services/UserService.cs", "usageCount": 3 },
    { "file": "Tests/UserServiceTests.cs", "usageCount": 5 }
  ],
  "configImpact": [
    { "file": "appsettings.json", "key": "UserDeletionPolicy", "impact": "behavior" }
  ],
  "workflowImpact": [
    { "workflow": "build.yml", "reason": "User model changes may require retest" }
  ],
  "sqlImpact": [
    { "migration": "20240115_AddUserRoles", "impact": "cascade delete affected" }
  ],
  "documentationImpact": [
    { "file": "docs/API.md", "section": "DELETE /users/{id}", "needsUpdate": true }
  ]
}
```

---

### Project Mapping

#### `trace_feature(featureName: string)`
Find all code implementing a feature.

**Response:**
```json
{
  "feature": "user-authentication",
  "components": {
    "controller": "src/Controllers/AuthController.cs::Login",
    "service": "src/Services/AuthService.cs::Authenticate",
    "repository": "src/Data/UserRepository.cs::FindByEmail",
    "models": ["src/Models/User.cs", "src/Models/LoginRequest.cs"],
    "migrations": ["Migrations/20240101_CreateUsers.cs"],
    "config": ["appsettings.json::Authentication"],
    "tests": ["Tests/AuthServiceTests.cs"],
    "documentation": "docs/AUTHENTICATION.md"
  }
}
```

---

#### `get_architecture()`
Layer structure and violations.

**Response:**
```json
{
  "layers": [
    {
      "name": "Controllers",
      "namespaces": ["MyApp.Controllers"],
      "dependsOn": ["Services"]
    },
    {
      "name": "Services",
      "namespaces": ["MyApp.Services"],
      "dependsOn": ["Repositories", "Models"]
    },
    {
      "name": "Repositories",
      "namespaces": ["MyApp.Data"],
      "dependsOn": ["Models"]
    },
    {
      "name": "Models",
      "namespaces": ["MyApp.Models"],
      "dependsOn": []
    }
  ],
  "violations": [
    {
      "from": "Models",
      "to": "Services",
      "file": "src/Models/User.cs",
      "line": 15,
      "reason": "ServiceLocator anti-pattern"
    }
  ]
}
```

---

### Configuration & Environment

#### `get_configuration_schema()`
Config file locations and structure.

**Response:**
```json
{
  "appsettings": {
    "path": "appsettings.json",
    "schema": {
      "Logging": {
        "LogLevel": { "type": "object", "keys": ["Default", "Microsoft"] }
      },
      "Database": {
        "ConnectionString": "string",
        "Timeout": "int"
      }
    }
  },
  "environmentSpecific": [
    "appsettings.Development.json",
    "appsettings.Production.json"
  ],
  "secretsFile": ".../secrets.json"
}
```

---

#### `get_workflows()`
GitHub Actions workflow definitions.

**Response:**
```json
{
  "workflows": [
    {
      "name": "build",
      "file": ".github/workflows/build.yml",
      "triggers": ["push", "pull_request"],
      "environment": {
        "dotnet-version": "9.0.x"
      },
      "jobs": [
        {
          "name": "build",
          "runsOn": "ubuntu-latest",
          "steps": [
            { "name": "checkout", "uses": "actions/checkout@v4" },
            { "name": "setup-dotnet", "uses": "actions/setup-dotnet@v4", "dotnetVersion": "9.0.x" },
            { "name": "build", "run": "dotnet build" },
            { "name": "test", "run": "dotnet test" }
          ]
        }
      ],
      "secrets": ["NUGET_API_KEY", "DEPLOY_TOKEN"]
    }
  ]
}
```

---

#### `get_workflow_impact(change: ProjectChange)`
Does this code change break the CI/CD?

**Request:**
```json
{
  "file": "src/MyApp.csproj",
  "changed": "TargetFramework",
  "oldValue": "net8.0",
  "newValue": "net9.0"
}
```

**Response:**
```json
{
  "affectedWorkflows": [
    {
      "workflow": "build.yml",
      "reason": "Workflow specifies dotnet-version 8.0.x, incompatible with net9.0",
      "breaking": true,
      "suggestion": "Update dotnet-version to 9.0.x in build.yml"
    }
  ]
}
```

---

### Code Quality

#### `detect_issues()`
Quick scan for code smells.

**Response:**
```json
{
  "unusedVariables": [
    { "file": "src/Services/UserService.cs", "line": 42, "name": "_unused" }
  ],
  "duplicateCode": [
    {
      "files": ["src/Services/UserService.cs", "src/Services/OrderService.cs"],
      "similarity": 0.95,
      "lines": 30
    }
  ],
  "nullReferenceRisks": [
    { "file": "src/Services/UserService.cs", "method": "GetUser", "riskLevel": "high" }
  ],
  "largeClasses": [
    { "file": "src/Services/MonolithService.cs", "loc": 1200, "methods": 45 }
  ]
}
```

**Non-blocking.** Returns what's available in current analysis state.

---

#### `validate_consistency()`
Check .csproj vs. workflow vs. actual code.

**Response:**
```json
{
  "warnings": [
    {
      "type": "TARGET_FRAMEWORK_MISMATCH",
      "severity": "warning",
      "projectFile": "src/MyApp.csproj",
      "projectSetting": "TargetFramework=net8.0",
      "workflow": ".github/workflows/build.yml",
      "workflowSetting": "dotnet-version=8.0.x",
      "status": "OK"
    },
    {
      "type": "LANGUAGE_VERSION_MISMATCH",
      "severity": "warning",
      "projectFile": "src/MyApp.csproj",
      "projectSetting": "LangVersion=latest",
      "workflowSetting": "dotnet-version=8.0.x",
      "issue": "latest language features may not be available in 8.0.x"
    }
  ]
}
```

---

### Database & Migrations

#### `analyze_migrations()`
Entity Framework migration history.

**Response:**
```json
{
  "migrationsFolder": "src/Data/Migrations",
  "migrations": [
    {
      "name": "20240101_InitialCreate",
      "file": "src/Data/Migrations/20240101_InitialCreate.cs",
      "tables": ["Users", "Orders"],
      "changes": "created"
    },
    {
      "name": "20240115_AddUserRoles",
      "file": "src/Data/Migrations/20240115_AddUserRoles.cs",
      "tables": ["UserRoles"],
      "changes": "added"
    }
  ],
  "currentSchema": {
    "tables": [
      {
        "name": "Users",
        "columns": [
          { "name": "Id", "type": "uuid", "nullable": false },
          { "name": "Email", "type": "string", "nullable": false }
        ]
      }
    ]
  }
}
```

---

### Code Editing

#### `edit_file(path: string, edits: TextEdit[])`
Apply semantic-aware edits to a file.

**Request:**
```json
{
  "path": "src/Services/UserService.cs",
  "edits": [
    {
      "type": "replace",
      "oldText": "public async Task<User> GetUser(Guid id)\n{\n    var user = await _repo.FindAsync(id);\n    return user;\n}",
      "newText": "public async Task<User> GetUser(Guid id)\n{\n    _logger.LogInformation($\"Fetching user {id}\");\n    var user = await _repo.FindAsync(id);\n    if (user == null) throw new NotFoundException($\"User {id} not found\");\n    return user;\n}"
    }
  ]
}
```

**Response:**
```json
{
  "success": true,
  "file": "src/Services/UserService.cs",
  "editsApplied": 1,
  "validationErrors": [],
  "newContent": "..."
}
```

**Implementation note:** Delegates to csharp-ls for semantic validation where possible. Falls back to Roslyn for syntax verification.

---

#### `get_call_chain(method: string, direction: "up" | "down")`
Backward/forward analysis of method calls.

**Response:**
```json
{
  "method": "ProcessOrder",
  "calledBy": [
    "OrderController.Post",
    "BatchProcessor.Process"
  ],
  "calls": [
    "ValidateOrder",
    "SaveToDb",
    "SendNotification"
  ],
  "depth": 3,
  "complexity": "high"
}
```

---

## Implementation Phases

### Phase 1: MVP (Core C# + Project Structure)
- MCP protocol handler
- Basic background analysis state machine
- csharp-ls LSP client wrapper
- Project/solution parsing (.csproj, .sln)
- Tools: `get_project_overview`, `summarize_file`, `find_references`, `edit_file`

**Deliverable:** Single binary that works with Claude Code for basic C# analysis and editing.

**Timeline:** 3-4 weeks

---

### Phase 2: Polyglot & Cross-File Impact
- YAML workflow parsing
- JSON/YAML config analysis
- `get_full_impact()` cross-file change prediction
- `trace_feature()` multi-file navigation
- Better dependency graph

**Deliverable:** AI understands project holistically (workflows, config, across multiple file types).

**Timeline:** 2-3 weeks

---

### Phase 3: Quality & Depth
- SQL migration analysis
- Architecture layer validation
- Duplicate code detection
- Better large-file handling
- Performance optimization

**Timeline:** 2-3 weeks

---

### Phase 4: Polish & Extensibility
- Incremental re-analysis on file changes
- Caching layer (fast re-opens)
- Custom analyzer extensibility
- Documentation & examples

**Timeline:** 1-2 weeks

---

## Technical Decisions

### Why embedded csharp-ls?
- Single binary deployment (no separate tool install)
- Version control (you own when upgrades happen)
- Integration with background analysis (managed lifecycle)
- Trade-off: Larger binary, but acceptable for Claude Code use case

### Why background analysis?
- Claude Code responsiveness (return quick partial results)
- Progressive UX (similar to VS)
- Complexity: state machine + incremental updates
- Payoff: Feels fast even on large projects

### Why Roslyn + csharp-ls?
- Roslyn: deep AST analysis for large files, source generators
- csharp-ls: proven LSP implementation, clean stdio interface
- Combination: best of both worlds

### Why YAML/JSON/XML parsing in .NET?
- Language-native parsers (YamlDotNet is solid)
- Tight integration with C# analysis (context from project config)
- Alternative: External tools (slower, IPC overhead)

### Why not build on isaacphi/mcp-language-server?
- That tool is Go-based, generic bridge
- Your tool is .NET-native, C#-specific
- Domain knowledge (workflows, migrations, generators) is C#-specific
- Simpler to maintain one language end-to-end

---

## Deployment

### Build & Package
```bash
dotnet publish -c Release -r osx-arm64 --self-contained
# Output: single binary csharp-mcp (150-200MB)
```

### Installation
```bash
# Claude Code claude_desktop_config.json
"mcpServers": {
  "csharp": {
    "command": "/path/to/csharp-mcp",
    "args": ["--workspace", "/path/to/project"]
  }
}
```

### Versioning
- Semantic versioning (1.0.0, 1.1.0, etc.)
- Track separately from csharp-ls versions
- Breaking MCP tool changes bump major version

---

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

---

## Open Questions / Future Exploration

1. **Incremental analysis** — How to update index when files change? Full recompile or surgical updates?
2. **Caching** — Persist index to disk for faster re-opens? Trade-off: correctness vs. speed.
3. **Monorepo edge cases** — How to handle circular dependencies between solutions?
4. **Source generator debugging** — Show generated code in results? Link back to generator?
5. **Custom analyzers** — Plugin system for project-specific code analysis?
6. **Roslyn symbol enrichment** — Use Roslyn alongside csharp-ls to get richer semantic info?

---

## Success Criteria

- ✅ Claude Code can edit large C# files safely (understanding structure via AST, not text patterns)
- ✅ Single binary deployment, no external tool installation
- ✅ Project loads within 5 seconds, full analysis within 30 seconds
- ✅ Monorepo support (multiple solutions, cross-project dependencies)
- ✅ Handles source generators transparently
- ✅ AI gets project-wide context (workflows, config, architecture) before editing
- ✅ `get_full_impact()` accurately predicts cross-file changes
- ✅ macOS + Linux primary support