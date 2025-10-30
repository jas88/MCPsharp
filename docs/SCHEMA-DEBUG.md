# JSON Schema Debugging

## Issue

Claude Code reports: `tools.89.custom.input_schema: JSON schema is invalid`

## Analysis

### Current State

✅ **All 21 tools have valid structure:**
- All have `type`: "object"
- All have `$schema`: "https://json-schema.org/draft/2020-12/schema"
- All have `properties` defined
- `required` arrays present where needed

✅ **Fixed Issues:**
- Added `$schema` field to all schemas
- Fixed `file_edit` enum from string to array: `["replace", "insert", "delete"]`

### Mystery

**The error references "tools.89"** but we only have 21 tools (0-20 indexed).

**The path ".custom.input_schema"** is odd - we use ".inputSchema" (camelCase).

This suggests:
1. Claude Code might be transforming/wrapping our tools
2. There might be multiple MCP servers configured
3. The error might not be from MCPsharp

## What to Check

### 1. Is MCPsharp Actually Being Used?

```bash
# Check Claude Code's MCP configuration
cat ~/.config/Claude/claude_desktop_config.json
# OR
cat ~/Library/Application\ Support/Claude/claude_desktop_config.json
```

Look for MCPsharp entry.

### 2. Get Complete Error Context

The full error should show:
- Which MCP server is failing
- The exact tool causing the issue
- The problematic schema field

### 3. Test MCPsharp Standalone

```bash
# Run and test all tools
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | \
  dotnet run --project src/MCPsharp/MCPsharp.csproj

# Should return 21 tools with valid schemas
```

### 4. Compare with Known Working MCP Server

Example from MCP spec:
```json
{
  "name": "example_tool",
  "description": "An example tool",
  "inputSchema": {
    "type": "object",
    "properties": {
      "param1": {
        "type": "string",
        "description": "First parameter"
      }
    },
    "required": ["param1"]
  }
}
```

Our format matches this exactly (plus $schema field).

## Current MCPsharp Tool Example

```json
{
  "name": "file_edit",
  "description": "Apply text edits to a file",
  "inputSchema": {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "type": "object",
    "properties": {
      "path": {
        "type": "string",
        "description": "Path to the file"
      },
      "edits": {
        "type": "array",
        "description": "Array of edit operations",
        "items": {
          "type": "object",
          "properties": {
            "type": {
              "type": "string",
              "enum": ["replace", "insert", "delete"]
            }
          }
        }
      }
    },
    "required": ["path", "edits"]
  }
}
```

This is **valid JSON Schema 2020-12**.

## Hypothesis

The error "tools.89" suggests Claude Code might be:
1. **Counting wrong** - Maybe it's seeing duplicates or multiple servers
2. **Using different server** - The error might not be from MCPsharp at all
3. **Different protocol** - Maybe Claude Code uses different MCP version

## Next Steps

**Need from you:**
1. Full error message with context
2. Your Claude Code MCP configuration
3. Confirmation that MCPsharp is the server being used
4. Any other MCP servers you have configured

**I can verify:**
- ✅ All 21 schemas are valid JSON Schema 2020-12
- ✅ Schema format matches MCP spec examples
- ✅ Enum is properly formatted as array
- ✅ Nested schemas are valid
- ✅ $schema field present in root schemas

The server should be working. The "tools.89" reference suggests the error might be from a different source.
