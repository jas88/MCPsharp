# What Remains for MCPsharp

## Critical: Main Program Entry Point

**Status:** ❌ NOT DONE
**Priority:** HIGHEST - Server cannot run without this!

### What's Missing

**Current Program.cs:**
```csharp
Console.WriteLine("Hello, World!");
```

**Needed:**
- Wire up JsonRpcHandler to stdin/stdout
- Initialize services (ProjectContextManager, FileOperationsService, McpToolRegistry, RoslynWorkspace)
- Run the JSON-RPC message loop
- Handle shutdown gracefully

**This is the glue that makes everything work together!**

---

## Phase 0+1 Status: BUILT BUT NOT CONNECTED

We have all the pieces but they're not connected:

✅ **Built and Tested:**
- JSON-RPC protocol handler (reads/writes JSON-RPC messages)
- MCP Tool Registry (14 tools registered)
- Project Context Manager (track opened projects)
- File Operations Service (list, read, write, edit)
- Roslyn Workspace (compilation, semantic model)
- Symbol Query Service (find symbols)
- Class Structure Service (analyze classes)
- Semantic Edit Service (add members)
- Reference Finder Service (find references)
- Project Parser Service (parse .csproj/.sln)

❌ **Not Connected:**
- Program.cs doesn't wire these together
- No stdin/stdout MCP server running
- Cannot actually be used by Claude Code yet

---

## What We Need to Complete MVP

### 1. Main Program Entry Point (CRITICAL)

**File:** `src/MCPsharp/Program.cs`

**Needs:**
```csharp
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;

// Parse command line arguments
var workspaceRoot = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning); // Don't pollute stdout
});

var logger = loggerFactory.CreateLogger<Program>();

// Initialize services
var projectManager = new ProjectContextManager();
var fileOps = new FileOperationsService(workspaceRoot);
var roslynWorkspace = new RoslynWorkspace();
var toolRegistry = new McpToolRegistry(
    projectManager,
    fileOps,
    roslynWorkspace,
    /* other Roslyn services */
);

// Create and run JSON-RPC handler
var handler = new JsonRpcHandler(toolRegistry, logger);
await handler.RunAsync(CancellationToken.None);
```

**Estimated Time:** 1-2 hours

---

### 2. Update JsonRpcHandler to Accept McpToolRegistry

**Current:** JsonRpcHandler has placeholder implementations for tools/list and tools/call

**Needed:**
- Constructor accepting McpToolRegistry
- tools/list calls registry.GetTools()
- tools/call calls registry.ExecuteTool()

**Estimated Time:** 30 minutes

---

### 3. End-to-End Testing

**Needed:**
- Manual test with actual Claude Code MCP client
- Or create a test that simulates complete stdin/stdout MCP session
- Verify all 14 tools work through JSON-RPC protocol

**Estimated Time:** 1-2 hours

---

## Optional: Phase 2 Features

According to CONCEPT.md, Phase 2 would add:

### Polyglot & Cross-File Impact
- ✅ **YAML workflow parsing** — Parse GitHub Actions workflows
- ✅ **JSON config analysis** — Parse appsettings.json
- ❌ **`get_full_impact()`** — Predict cross-file change impact
- ❌ **`trace_feature()`** — Multi-file feature navigation
- ❌ **Better dependency graph** — Project-to-project dependencies

**Estimated Time:** 20-30 hours (with parallel agents: ~3-4 hours)

---

## Summary: What's Left for Working MVP

### Critical (Must Have)
1. **Program.cs main entry point** — Wire services together, run MCP server
2. **JsonRpcHandler integration** — Connect to McpToolRegistry
3. **End-to-end test** — Verify works with Claude Code

**Estimated Time:** 2-4 hours

### Nice to Have (Phase 2)
1. **Workflow analysis** — Parse .github/workflows/*.yml
2. **Config analysis** — Parse appsettings.json, YAML configs
3. **Impact analysis** — Predict which files affected by changes
4. **Feature tracing** — Map features across multiple files

**Estimated Time:** 3-4 hours with parallel agents

---

## Current State

**Phase 0:** ✅ COMPLETE (6 tools, 80 tests)
**Phase 1:** ✅ COMPLETE (8 tools, 88 tests)
**Integration:** ❌ NOT DONE (Program.cs not wired up)
**Phase 2:** ❌ NOT STARTED (optional enhancements)

---

## Recommendation

**Next Steps:**

1. **Wire up Program.cs** (CRITICAL - 1 hour)
   - Connect all services
   - Run JSON-RPC handler on stdin/stdout
   - Handle graceful shutdown

2. **Test with Claude Code** (CRITICAL - 1 hour)
   - Add to claude_desktop_config.json
   - Try opening a project
   - Try finding a class
   - Try adding a property

3. **Optional: Add Phase 2 Features** (3-4 hours with agents)
   - Workflow parsing
   - Config analysis
   - Impact prediction

**Total to Working MVP:** 2 hours
**Total with Phase 2:** 5-6 hours

---

## The One Missing Piece

We built an amazing foundation with **14 MCP tools** and **88 passing tests**, but we haven't actually created the **server executable** that runs the MCP protocol.

It's like building a complete car engine with all parts tested individually, but not connecting it to the steering wheel and gas pedal!

**Let's wire it up!** 🚀
