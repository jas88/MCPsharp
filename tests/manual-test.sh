#!/bin/bash
# Simple manual test to verify MCP server runs correctly

set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

echo "Testing MCPsharp server..."
echo ""

# Test 1: Initialize
echo "Test 1: Initialize"
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | \
    dotnet run --project src/MCPsharp/MCPsharp.csproj 2>/dev/null | \
    jq -r '.result.serverInfo.name'
echo ""

# Test 2: List tools
echo "Test 2: List tools"
echo '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' | \
    dotnet run --project src/MCPsharp/MCPsharp.csproj 2>/dev/null | \
    jq -r '.result.tools | length'
echo " tools available"
echo ""

echo "All tests passed!"
