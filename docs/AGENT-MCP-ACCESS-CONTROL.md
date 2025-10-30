# Agent MCP Access Control Analysis

## Question

Can MCP server access be restricted to specific agents in Claude Code? (e.g., GitHub agents have access to GitHub MCP server, but main Claude Code instance does not)

## Answer: NOT CURRENTLY SUPPORTED ❌

Based on research of Claude Code documentation and agent definition schemas, **per-agent MCP access control is not currently supported** in Claude Code.

## Current State

### What IS Supported

**1. Tool Restrictions in Agent Definitions**

Agents can specify `allowed_tools` and `restricted_tools`:

```yaml
capabilities:
  allowed_tools:
    - Read
    - Grep
    - Glob
    - WebSearch
  restricted_tools:
    - Write      # Read-only analysis
    - Edit
    - Bash       # No execution
    - Task       # No delegation
```

**Example from `code-analyzer.md`:**
- ✅ Can use: Read, Grep, Glob, WebSearch
- ❌ Cannot use: Write, Edit, Bash, Task

**2. MCP Server Scopes (Global Only)**

MCP servers are configured at three scopes:
- **User scope**: Available across all projects for the user
- **Project scope**: Shared via `.mcp.json` file
- **Local scope**: Only in current project

**But:** All scopes apply globally to Claude Code AND all agents

**3. Enterprise Restrictions (Organization-Wide)**

Administrators can control:
- `allowedMcpServers` - Whitelist of permitted servers
- `deniedMcpServers` - Blacklist of blocked servers

**But:** These apply to everyone, not per-agent

### What IS NOT Supported

❌ **Per-Agent MCP Server Access**
- Cannot give GitHub MCP to github agents only
- Cannot restrict main instance from using GitHub MCP
- Cannot create agent-specific MCP configurations

❌ **Granular MCP Tool Restrictions**
- Agent tool restrictions only apply to Claude Code's built-in tools
- MCP tools are referenced as strings (e.g., `mcp__github__*`)
- No mechanism to selectively allow/deny MCP tools per agent

## Current Agent Tool Restriction Patterns

### Example 1: Read-Only Analyzer
```yaml
# From code-analyzer.md
capabilities:
  allowed_tools:
    - Read
    - Grep
    - Glob
    - WebSearch  # For best practices research
  restricted_tools:
    - Write      # Read-only analysis
    - Edit
    - MultiEdit
    - Bash       # No execution needed
    - Task       # No delegation
```

### Example 2: GitHub Agent with MCP Access
```yaml
# From pr-manager.md
tools:
  - Bash
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - TodoWrite
  - mcp__github__*                    # Full GitHub MCP access
  - mcp__claude-flow__swarm_init      # Claude Flow MCP access
  - mcp__claude-flow__agent_spawn
  - mcp__claude-flow__task_orchestrate
```

**Issue:** The `tools` list is declarative (what the agent CAN use), but:
1. Main Claude Code instance also has access to same MCP servers
2. No enforcement mechanism to restrict MCP by agent
3. MCP tools are string patterns, not actual access controls

## Workarounds

### Option 1: Environment-Based MCP Configuration (Partial Solution)

**Setup different MCP configurations per project:**

```json
// Project A: .mcp.json (no GitHub MCP)
{
  "mcpServers": {
    "csharp": { "command": "/path/to/mcpsharp", "args": [...] }
  }
}

// Project B: .mcp.json (with GitHub MCP)
{
  "mcpServers": {
    "csharp": { "command": "/path/to/mcpsharp", "args": [...] },
    "github": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-github"] }
  }
}
```

**Limitation:** Still doesn't restrict by agent, only by project

### Option 2: Custom MCP Proxy Server (Advanced)

**Create a proxy MCP server that enforces agent-based access control:**

```
Claude Code → MCP Proxy Server → Multiple MCP Servers
                  ↓ (checks agent context)
            Allows/Denies based on rules
```

**Implementation:**
1. Build custom MCP server in Node.js/Python
2. Accept agent identifier in tool call metadata
3. Route requests to appropriate backend MCP servers
4. Enforce access rules per agent

**Pros:** Full control over access
**Cons:** Complex, requires custom development

### Option 3: Separate Claude Code Instances (Nuclear Option)

**Run different Claude Code instances:**
- Instance A: Only has core tools, no GitHub MCP
- Instance B: Only for GitHub agents, has GitHub MCP

**Limitation:** Impractical for workflow, defeats purpose of agents

### Option 4: Manual Tool Filtering (Current Best Practice)

**Rely on agent discipline:**
- Agents specify which MCP tools they use in `tools` list
- Trust that spawned agents won't use tools not in their list
- Monitor and audit agent behavior

**Current State:** This is what your agents already do

```yaml
# Good: code-analyzer explicitly doesn't list MCP tools
restricted_tools:
  - Write
  - Edit
  - Bash
  - Task

# Good: pr-manager explicitly lists only needed MCP tools
tools:
  - mcp__github__*
  - mcp__claude-flow__*
```

## Recommendation

### Current Answer: NO ❌

**You CANNOT restrict MCP server access to agents only in Claude Code today.**

**Why:**
1. Claude Code does not implement per-agent MCP access control
2. All MCP servers in configuration are available globally
3. Agent tool restrictions only apply to built-in tools, not MCP tools
4. No enforcement mechanism exists for MCP tool access by agent

### What You CAN Do

**1. Document Expected Tool Usage (Current Approach)**
- Specify which agents should use which MCP tools
- Trust agent definitions to guide behavior
- Review spawned agent behavior

**2. Use Project-Scoped MCP (Partial Isolation)**
- Only enable GitHub MCP in projects that need it
- Use `.mcp.json` for project-specific servers
- Keep user-level MCP minimal

**3. Request Feature from Anthropic**
This would be a valuable feature:
- Agent-specific MCP server access lists
- MCP tool allowlist/denylist per agent type
- Inherited but overridable permissions
- Audit logging of MCP usage by agent

**4. Build Custom MCP Proxy (Advanced)**
- Create intermediary MCP server
- Implement your own access control
- Route based on agent context
- Most flexible but most complex

## Current Agent Pattern

Your agents already follow a good pattern:

**Restrictive Agents (No MCP):**
```yaml
# code-analyzer - analysis only
restricted_tools:
  - Write
  - Edit
  - Bash
  - Task
# No MCP tools listed
```

**Specialized Agents (Scoped MCP):**
```yaml
# pr-manager - GitHub operations only
tools:
  - mcp__github__*          # Only GitHub MCP
  - mcp__claude-flow__*     # Only Claude Flow MCP
# Not listing other MCP servers
```

**Conclusion:** While not enforced, your agent definitions already document intended MCP usage patterns. This is the current best practice until Anthropic adds per-agent access control.

## Feature Request

**Consider requesting from Anthropic:**
```yaml
# Hypothetical future syntax
agent:
  name: github-agent
  tools:
    allowed:
      - Read
      - Write
      - mcp__github__*
    denied:
      - mcp__*  # Deny all other MCP servers
  mcp_servers:
    allowed:
      - github
      - claude-flow
    denied:
      - "*"  # Deny all others
```

This would provide true per-agent MCP isolation.
