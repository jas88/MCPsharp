# MCPsharp MVP Implementation Plan

## Overview

This document outlines the implementation plan for the MCPsharp MVP, broken into two phases:
- **Phase 0**: Basic MCP server with file operations (open, list, read, write, edit)
- **Phase 1**: csharp-ls integration for semantic C# operations (find class, modify class)

## Phase 0: Basic MCP Server

### Goals
- Implement MCP protocol (JSON-RPC 2.0 over stdio)
- Support project/directory context management
- Provide basic file operations (list, read, write, edit as text)
- No C# language understanding yet

### Architecture

```
Claude Code
    ↓ stdin/stdout (JSON-RPC 2.0)
┌─────────────────────────────────┐
│ MCP Server Process              │
├─────────────────────────────────┤
│ • JSON-RPC Handler              │
│ • Tool Registry                 │
│ • Project Context Manager       │
│ • File Operations Service       │
└─────────────────────────────────┘
    ↓
File System
```

### Components

#### 1. JSON-RPC Handler
**Responsibilities:**
- Read JSON-RPC messages from stdin
- Parse and validate JSON-RPC 2.0 format
- Route to appropriate handlers
- Write JSON-RPC responses to stdout
- Error handling and logging

**Implementation:**
```csharp
public class JsonRpcHandler
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync();
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(line);

            var response = await RouteRequest(request);

            var json = JsonSerializer.Serialize(response);
            await Console.Out.WriteLineAsync(json);
        }
    }
}
```

**Message Types:**
- `initialize` — Handshake with Claude Code
- `tools/list` — Return available tools
- `tools/call` — Execute a tool
- `notifications/*` — Log messages, progress updates

#### 2. Project Context Manager
**Responsibilities:**
- Track currently opened project directory
- Validate paths are within project boundary
- Maintain project metadata (file count, last scan time)
- Watch for file system changes (optional for MVP)

**State:**
```csharp
public class ProjectContext
{
    public string? RootPath { get; set; }
    public DateTime? OpenedAt { get; set; }
    public int FileCount { get; set; }
    public HashSet<string> KnownFiles { get; set; }
}
```

**Operations:**
- `OpenProject(path)` — Validate and set root path
- `IsValidPath(path)` — Check path is within project
- `GetProjectInfo()` — Return metadata

#### 3. File Operations Service
**Responsibilities:**
- List files matching patterns
- Read file contents as text
- Write file contents
- Apply text edits (line/column based or full replace)

**Implementation Considerations:**
- Use `System.IO.File` for basic operations
- Support glob patterns for listing (`**/*.cs`, `src/**/*.csproj`)
- Text editing: support both full-file replace and range-based edits
- Handle encoding (UTF-8 default, detect BOM)
- Large file handling (stream for >10MB files)

### MCP Tools (Phase 0)

#### `project_open`
Open a project directory for analysis.

**Parameters:**
```json
{
  "path": "/absolute/path/to/project"
}
```

**Response:**
```json
{
  "success": true,
  "path": "/absolute/path/to/project",
  "fileCount": 142,
  "message": "Project opened successfully"
}
```

**Validation:**
- Path must exist and be a directory
- Path must be absolute
- Update ProjectContext state

---

#### `project_info`
Get information about the currently opened project.

**Parameters:** None

**Response:**
```json
{
  "path": "/absolute/path/to/project",
  "openedAt": "2024-10-28T10:05:00Z",
  "fileCount": 142,
  "hasProject": true
}
```

---

#### `file_list`
List files in the project, optionally filtered by glob pattern.

**Parameters:**
```json
{
  "pattern": "**/*.cs",
  "includeHidden": false
}
```

**Response:**
```json
{
  "files": [
    {
      "path": "src/Program.cs",
      "relativePath": "src/Program.cs",
      "size": 1024,
      "lastModified": "2024-10-28T09:30:00Z"
    },
    {
      "path": "src/Services/UserService.cs",
      "relativePath": "src/Services/UserService.cs",
      "size": 2048,
      "lastModified": "2024-10-28T10:00:00Z"
    }
  ],
  "totalFiles": 2,
  "pattern": "**/*.cs"
}
```

**Implementation:**
```csharp
// Use Directory.EnumerateFiles with SearchOption.AllDirectories
// Filter with Glob pattern matching library (DotNet.Glob or Microsoft.Extensions.FileSystemGlobbing)
```

---

#### `file_read`
Read a file's contents as text.

**Parameters:**
```json
{
  "path": "src/Program.cs"
}
```

**Response:**
```json
{
  "path": "src/Program.cs",
  "content": "using System;\n\nnamespace MyApp\n{\n    class Program...",
  "encoding": "utf-8",
  "lineCount": 45,
  "size": 1024
}
```

**Error Cases:**
- File not found → `FileNotFoundError`
- Path outside project → `SecurityError`
- File too large → Return truncated with warning
- Binary file → Return error or hex preview

---

#### `file_write`
Write content to a file (creates if doesn't exist, overwrites if exists).

**Parameters:**
```json
{
  "path": "src/NewFile.cs",
  "content": "using System;\n\nnamespace MyApp...",
  "createDirectories": true
}
```

**Response:**
```json
{
  "success": true,
  "path": "src/NewFile.cs",
  "bytesWritten": 1024,
  "created": true
}
```

**Implementation:**
- Validate path is within project
- Create parent directories if `createDirectories: true`
- Write with UTF-8 encoding
- Return whether file was created or overwritten

---

#### `file_edit`
Apply text edits to a file without reading full content.

**Parameters:**
```json
{
  "path": "src/Program.cs",
  "edits": [
    {
      "type": "replace",
      "start": { "line": 10, "column": 0 },
      "end": { "line": 10, "column": 50 },
      "newText": "        Console.WriteLine(\"Hello, Claude!\");"
    },
    {
      "type": "insert",
      "position": { "line": 15, "column": 0 },
      "text": "        // New comment\n"
    },
    {
      "type": "delete",
      "start": { "line": 20, "column": 0 },
      "end": { "line": 22, "column": 0 }
    }
  ]
}
```

**Response:**
```json
{
  "success": true,
  "path": "src/Program.cs",
  "editsApplied": 3,
  "newContent": "using System;\n\nnamespace MyApp..."
}
```

**Implementation:**
- Read file into memory
- Apply edits in reverse order (end to start) to maintain offsets
- Validate all edits before applying any
- Return new content for verification
- Write back to file

**Edit Types:**
- `replace` — Replace text in range
- `insert` — Insert text at position
- `delete` — Delete text in range

---

### Implementation Tasks (Phase 0)

1. **Project Setup** (1-2 hours)
   - Create .NET 9 console app
   - Add dependencies: System.Text.Json, Microsoft.Extensions.FileSystemGlobbing
   - Setup project structure: `/src`, `/tests`

2. **JSON-RPC Handler** (4-6 hours)
   - Implement stdin/stdout message loop
   - JSON-RPC 2.0 request/response types
   - Error handling and logging
   - Unit tests for message parsing

3. **Project Context Manager** (2-3 hours)
   - ProjectContext state class
   - Path validation utilities
   - Project open/close operations
   - Unit tests

4. **File Operations Service** (4-6 hours)
   - Implement file_list with glob patterns
   - Implement file_read with encoding detection
   - Implement file_write with directory creation
   - Implement file_edit with multi-edit support
   - Unit tests for each operation

5. **MCP Tool Registry** (2-3 hours)
   - Tool metadata (name, description, parameters schema)
   - Dynamic tool registration
   - tools/list implementation
   - tools/call dispatcher

6. **Integration Testing** (3-4 hours)
   - Create test project with sample C# files
   - Test full MCP flow: open → list → read → edit → write
   - Test error cases
   - Manual testing with Claude Code (if possible)

**Total Phase 0 Estimate: 16-24 hours (2-3 days)**

---

## Phase 1: csharp-ls Integration

### Goals
- Integrate csharp-ls as embedded language server
- Implement LSP client for communication
- Provide semantic C# operations (find class, get symbol info, modify class)
- Enable Claude Code to understand C# project structure

### Architecture

```
Claude Code
    ↓ stdin/stdout (MCP/JSON-RPC)
┌─────────────────────────────────┐
│ MCP Server Process              │
├─────────────────────────────────┤
│ • MCP Tools (Phase 0 + Phase 1) │
│ • LSP Client                    │
│ • Semantic Operation Handler    │
└─────────────────────────────────┘
    ↓ stdin/stdout (LSP/JSON-RPC)
┌─────────────────────────────────┐
│ csharp-ls subprocess            │
│ (Roslyn-based language server)  │
└─────────────────────────────────┘
```

### Components

#### 1. LSP Client
**Responsibilities:**
- Start csharp-ls subprocess
- Send LSP initialization handshake
- Notify csharp-ls of file opens/changes/closes
- Send LSP requests (symbols, definitions, references)
- Handle LSP responses and notifications
- Manage subprocess lifecycle

**Key LSP Messages:**
```
initialize → Initialize language server with workspace root
initialized → Server ready notification
textDocument/didOpen → Open a file for analysis
textDocument/didChange → Notify of file changes
textDocument/documentSymbol → Get symbols in a file
workspace/symbol → Search for symbols in workspace
textDocument/definition → Find definition of symbol
textDocument/references → Find all references to symbol
textDocument/hover → Get hover information
shutdown → Clean shutdown
exit → Terminate
```

**Implementation:**
```csharp
public class LspClient
{
    private Process _process;
    private StreamWriter _stdin;
    private StreamReader _stdout;
    private int _messageId = 0;

    public async Task StartAsync(string workspaceRoot)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "csharp-ls",
                Arguments = "",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        _process.Start();
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        await SendInitialize(workspaceRoot);
    }

    public async Task<JObject> SendRequest(string method, object parameters)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = ++_messageId,
            method = method,
            @params = parameters
        };

        var content = JsonSerializer.Serialize(request);
        var message = $"Content-Length: {Encoding.UTF8.GetByteCount(content)}\r\n\r\n{content}";

        await _stdin.WriteAsync(message);
        await _stdin.FlushAsync();

        return await ReadResponse();
    }
}
```

#### 2. Semantic Operation Handler
**Responsibilities:**
- Translate high-level MCP tool calls into LSP requests
- Parse LSP responses and format for MCP
- Handle multi-step operations (e.g., "add property" = find class + parse + generate + edit)
- Cache LSP responses for performance

**Example Flow: "Find class ExampleClass"**
```
MCP Tool Call: find_symbol(name="ExampleClass", kind="class")
    ↓
LSP Request: workspace/symbol(query="ExampleClass")
    ↓
LSP Response: [ { name: "ExampleClass", kind: Class, location: {...} } ]
    ↓
MCP Response: { className: "ExampleClass", file: "src/Example.cs", line: 10 }
```

**Example Flow: "Add property to class"**
```
MCP Tool Call: add_class_property(className="ExampleClass", propertyName="ClaudeWasHere", propertyType="string")
    ↓
1. find_symbol("ExampleClass") → get file location
2. textDocument/documentSymbol(file) → get class structure
3. Determine insertion point (end of class, before closing brace)
4. Generate property code: "public string ClaudeWasHere { get; set; }"
5. file_edit(path, insert at line X)
    ↓
MCP Response: { success: true, property: "ClaudeWasHere", location: {...} }
```

### MCP Tools (Phase 1)

#### `language_server_start`
Start the csharp-ls language server for the current project.

**Parameters:**
```json
{
  "workspaceRoot": "/absolute/path/to/project"
}
```

**Response:**
```json
{
  "success": true,
  "serverVersion": "0.5.0",
  "capabilities": {
    "documentSymbolProvider": true,
    "workspaceSymbolProvider": true,
    "definitionProvider": true,
    "referencesProvider": true
  }
}
```

**Implementation:**
- Call `LspClient.StartAsync()`
- Wait for `initialized` notification
- Cache server capabilities

---

#### `find_symbol`
Find symbols (classes, methods, properties, etc.) by name.

**Parameters:**
```json
{
  "query": "ExampleClass",
  "kind": "class",
  "limit": 10
}
```

**Response:**
```json
{
  "symbols": [
    {
      "name": "ExampleClass",
      "kind": "class",
      "file": "src/Example.cs",
      "line": 10,
      "column": 7,
      "containerName": "MyApp.Models"
    }
  ],
  "totalResults": 1
}
```

**LSP Mapping:**
- `workspace/symbol` with query parameter
- Filter by `kind` if specified (Class = 5, Method = 6, Property = 7, etc.)

**Symbol Kinds:**
- `class`, `interface`, `struct`, `enum`
- `method`, `property`, `field`
- `namespace`

---

#### `get_symbol_info`
Get detailed information about a symbol at a specific location.

**Parameters:**
```json
{
  "file": "src/Example.cs",
  "line": 10,
  "column": 15
}
```

**Response:**
```json
{
  "name": "ExampleClass",
  "kind": "class",
  "signature": "public class ExampleClass",
  "documentation": "/// Example class for demonstration",
  "namespace": "MyApp.Models",
  "baseTypes": ["Object"],
  "members": [
    {
      "name": "Id",
      "kind": "property",
      "type": "int",
      "line": 12
    },
    {
      "name": "Name",
      "kind": "property",
      "type": "string",
      "line": 13
    }
  ]
}
```

**LSP Mapping:**
- `textDocument/hover` for documentation
- `textDocument/documentSymbol` for members
- Combine results into single response

---

#### `get_class_structure`
Get the structure of a specific class (all members, base types, etc.).

**Parameters:**
```json
{
  "className": "ExampleClass",
  "file": "src/Example.cs"
}
```

**Response:**
```json
{
  "name": "ExampleClass",
  "namespace": "MyApp.Models",
  "kind": "class",
  "accessibility": "public",
  "isAbstract": false,
  "isSealed": false,
  "baseTypes": ["Object"],
  "interfaces": ["IEquatable<ExampleClass>"],
  "properties": [
    {
      "name": "Id",
      "type": "int",
      "accessibility": "public",
      "hasGetter": true,
      "hasSetter": true,
      "line": 12
    }
  ],
  "methods": [
    {
      "name": "ToString",
      "returnType": "string",
      "accessibility": "public",
      "isOverride": true,
      "parameters": [],
      "line": 20
    }
  ],
  "fields": [],
  "location": {
    "startLine": 10,
    "endLine": 25
  }
}
```

**Implementation:**
- Use `textDocument/documentSymbol` to get full structure
- Parse symbol tree to extract class details
- May need Roslyn for full detail (Phase 2)

---

#### `add_class_property`
Add a property to an existing class.

**Parameters:**
```json
{
  "className": "ExampleClass",
  "propertyName": "ClaudeWasHere",
  "propertyType": "string",
  "accessibility": "public",
  "hasGetter": true,
  "hasSetter": true,
  "file": "src/Example.cs"
}
```

**Response:**
```json
{
  "success": true,
  "className": "ExampleClass",
  "propertyName": "ClaudeWasHere",
  "insertedAt": {
    "line": 18,
    "column": 4
  },
  "generatedCode": "    public string ClaudeWasHere { get; set; }"
}
```

**Implementation Steps:**
1. Find class location (use `find_symbol` or provided `file`)
2. Get class structure (use `get_class_structure`)
3. Determine insertion point:
   - After last property (preferred)
   - Before first method
   - Before closing brace of class
4. Generate property code with proper indentation
5. Apply edit using `file_edit` tool
6. Notify LSP of change: `textDocument/didChange`

**Code Generation:**
```csharp
string GenerateProperty(string name, string type, string accessibility, bool hasGetter, bool hasSetter)
{
    var getter = hasGetter ? "get; " : "";
    var setter = hasSetter ? "set; " : "";
    return $"{accessibility} {type} {name} {{ {getter}{setter}}}";
}
```

---

#### `add_class_method`
Add a method to an existing class.

**Parameters:**
```json
{
  "className": "ExampleClass",
  "methodName": "Greet",
  "returnType": "string",
  "parameters": [
    { "name": "name", "type": "string" }
  ],
  "accessibility": "public",
  "body": "return $\"Hello, {name}!\";",
  "file": "src/Example.cs"
}
```

**Response:**
```json
{
  "success": true,
  "className": "ExampleClass",
  "methodName": "Greet",
  "insertedAt": {
    "line": 22,
    "column": 4
  },
  "generatedCode": "    public string Greet(string name)\n    {\n        return $\"Hello, {name}!\";\n    }"
}
```

**Implementation:**
- Similar to `add_class_property`
- Parse parameters and generate signature
- Format method body with proper indentation
- Insert before closing brace of class

---

#### `find_references`
Find all references to a symbol.

**Parameters:**
```json
{
  "file": "src/Example.cs",
  "line": 10,
  "column": 15
}
```

**Response:**
```json
{
  "symbol": "ExampleClass",
  "references": [
    {
      "file": "src/Services/UserService.cs",
      "line": 25,
      "column": 20,
      "context": "        var example = new ExampleClass();"
    },
    {
      "file": "tests/ExampleTests.cs",
      "line": 10,
      "column": 30,
      "context": "        var sut = new ExampleClass();"
    }
  ],
  "totalReferences": 2
}
```

**LSP Mapping:**
- `textDocument/references`
- Include declaration location if `includeDeclaration: true`

---

### Implementation Tasks (Phase 1)

1. **csharp-ls Setup** (2-3 hours)
   - Download/bundle csharp-ls binary
   - Test csharp-ls standalone with sample project
   - Understand initialization flow and capabilities

2. **LSP Client Implementation** (8-12 hours)
   - Process management (start, stop, restart)
   - LSP message protocol (Content-Length headers, JSON-RPC)
   - Initialize/initialized handshake
   - Request/response handling with async support
   - Notification handling (diagnostics, progress)
   - Error handling and reconnection logic
   - Unit tests with mocked LSP server

3. **File Change Notifications** (2-3 hours)
   - Implement `textDocument/didOpen`
   - Implement `textDocument/didChange`
   - Implement `textDocument/didClose`
   - Integrate with Phase 0 file operations

4. **Symbol Finding Tools** (4-6 hours)
   - Implement `find_symbol` (workspace/symbol)
   - Implement `get_symbol_info` (hover + documentSymbol)
   - Implement `get_class_structure` (documentSymbol parsing)
   - Implement `find_references`
   - Unit tests

5. **Semantic Editing Tools** (8-12 hours)
   - Code generation utilities (properties, methods)
   - Indentation detection and formatting
   - Insertion point detection in classes
   - Implement `add_class_property`
   - Implement `add_class_method`
   - Integration tests with real C# files

6. **Integration Testing** (4-6 hours)
   - Test full flow: start LSP → find class → add property → verify
   - Test with multiple project structures (single file, multi-project)
   - Test error cases (class not found, invalid syntax, etc.)
   - Performance testing (large projects)

7. **Documentation & Examples** (2-3 hours)
   - Document each MCP tool with examples
   - Create sample C# project for testing
   - Add troubleshooting guide

**Total Phase 1 Estimate: 30-45 hours (4-6 days)**

---

## Testing Strategy

### Phase 0 Testing

**Unit Tests:**
- JSON-RPC message parsing/serialization
- Path validation (inside/outside project)
- Glob pattern matching
- Text edit application (multiple edits, edge cases)

**Integration Tests:**
```csharp
[Fact]
public async Task FullWorkflow_OpenListReadEditWrite()
{
    // Arrange: Create temp project with sample files
    var projectPath = CreateTestProject();

    // Act: Execute MCP tool calls
    await CallTool("project_open", new { path = projectPath });
    var files = await CallTool("file_list", new { pattern = "**/*.cs" });
    var content = await CallTool("file_read", new { path = "src/Program.cs" });
    await CallTool("file_edit", new { path = "src/Program.cs", edits = [...] });

    // Assert: Verify file was edited correctly
    var newContent = File.ReadAllText(Path.Combine(projectPath, "src/Program.cs"));
    Assert.Contains("Claude", newContent);
}
```

### Phase 1 Testing

**Unit Tests:**
- LSP message serialization
- LSP request/response correlation (match IDs)
- Code generation (properties, methods)
- Indentation detection

**Integration Tests:**
```csharp
[Fact]
public async Task SemanticEdit_AddPropertyToClass()
{
    // Arrange: Create C# project with ExampleClass
    var projectPath = CreateTestProjectWithClass("ExampleClass");
    await CallTool("project_open", new { path = projectPath });
    await CallTool("language_server_start", new { workspaceRoot = projectPath });

    // Act: Add property via semantic tool
    var result = await CallTool("add_class_property", new
    {
        className = "ExampleClass",
        propertyName = "ClaudeWasHere",
        propertyType = "string",
        file = "src/ExampleClass.cs"
    });

    // Assert: Verify property was added
    Assert.True(result.success);
    var code = File.ReadAllText(Path.Combine(projectPath, "src/ExampleClass.cs"));
    Assert.Contains("public string ClaudeWasHere", code);

    // Verify csharp-ls can still parse it
    var symbols = await CallTool("get_class_structure", new
    {
        className = "ExampleClass",
        file = "src/ExampleClass.cs"
    });
    Assert.Contains(symbols.properties, p => p.name == "ClaudeWasHere");
}
```

**Manual Testing with Claude Code:**
1. Install MCP server in Claude Code config
2. Open a real C# project
3. Ask Claude Code to: "Find the class Program"
4. Ask: "Add a property 'TestProperty' of type int to Program"
5. Verify the edit was applied correctly
6. Ask: "Show me all references to TestProperty"

---

## Dependencies

### NuGet Packages
- **System.Text.Json** — JSON serialization (built-in)
- **Microsoft.Extensions.FileSystemGlobbing** — Glob pattern matching
- **Microsoft.Extensions.Logging** — Logging infrastructure
- **Newtonsoft.Json** or **System.Text.Json** — LSP message handling

### External Tools
- **csharp-ls** — Download from https://github.com/razzmatazz/csharp-language-server
  - Version: 0.5.0 or later
  - Binary for macOS: `csharp-ls` (x64 or arm64)
  - Bundle in `/tools/csharp-ls` directory

### Testing Tools
- **xUnit** — Unit testing framework
- **FluentAssertions** — Assertion library
- **Moq** — Mocking framework for LSP client tests

---

## Success Criteria

### Phase 0
- ✅ Can open a C# project directory
- ✅ Can list files with glob patterns
- ✅ Can read file contents as text
- ✅ Can write/create files
- ✅ Can apply multiple text edits correctly
- ✅ All operations validate paths are within project
- ✅ Unit tests achieve >80% coverage
- ✅ Integration test: complete file edit workflow

### Phase 1
- ✅ csharp-ls starts and initializes successfully
- ✅ Can find classes by name across project
- ✅ Can get class structure (properties, methods)
- ✅ Can add property to class with correct indentation
- ✅ Can add method to class with correct indentation
- ✅ Can find all references to a symbol
- ✅ LSP stays in sync after file edits
- ✅ Integration test: semantic edit workflow
- ✅ Manual test with Claude Code succeeds

---

## Risk Mitigation

### Risk: csharp-ls fails to start or crashes
**Mitigation:**
- Implement health check and auto-restart
- Log stderr output for debugging
- Provide fallback: text-based operations still work

### Risk: LSP responses are slow on large projects
**Mitigation:**
- Implement timeout and cancellation
- Cache LSP responses (symbols, structure)
- Show progress to user for long operations

### Risk: Text edits corrupt file (indentation, syntax)
**Mitigation:**
- Always validate edit locations before applying
- Use csharp-ls diagnostics to verify syntax after edit
- Implement "dry run" mode that returns preview without applying

### Risk: Path traversal security issues
**Mitigation:**
- Canonicalize all paths (Path.GetFullPath)
- Always check path starts with project root
- Unit tests for path traversal attempts

---

## Future Enhancements (Post-MVP)

- Roslyn integration for deeper analysis
- Background analysis state machine (Loaded → FullyAnalyzed)
- Project/solution parsing (.csproj, .sln)
- Multi-project workspace support
- Configuration file analysis (appsettings.json)
- GitHub workflow analysis
- Database migration analysis
- Refactoring tools (rename, extract method)
- Code generation templates

---

## Timeline Summary

**Phase 0:** 2-3 days (16-24 hours)
**Phase 1:** 4-6 days (30-45 hours)
**Testing & Polish:** 1-2 days (8-16 hours)

**Total MVP Timeline: 7-11 days (54-85 hours)**

---

## Next Steps

1. Review this plan with stakeholders
2. Setup development environment (.NET 9, csharp-ls)
3. Create GitHub repository structure
4. Begin Phase 0 implementation
5. Write tests first (TDD approach)
6. Iterate based on feedback from Claude Code integration
