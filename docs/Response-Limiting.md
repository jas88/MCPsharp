# Response Token Limiting Configuration

MCPsharp includes a configurable response processing system that automatically limits tool responses to respect token limits, preventing excessive output while maintaining useful information.

## Features

- **Automatic Token Counting**: Estimates token count for all tool responses
- **Configurable Limits**: Set maximum token limits per response
- **Multiple Truncation Strategies**: Choose from different summarization styles
- **Tool Exemptions**: Certain tools can be exempt from limits
- **Environment Variable Configuration**: Easy configuration without code changes
- **Warning System**: Alerts when responses approach limits
- **Metadata Tracking**: Provides information about processing performed

## Configuration

### Environment Variables

Configure response limiting using these environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `MCP_MAX_TOKENS` | `4000` | Maximum tokens per response |
| `MCP_ENABLE_TRUNCATION` | `true` | Whether to truncate oversized responses |
| `MCP_SUMMARY_STYLE` | `Ellipsis` | Truncation strategy (Ellipsis, Paragraphs, JsonStructure, LineBased) |
| `MCP_TRUNCATE_LENGTH` | `1000` | Target character count for truncation |
| `MCP_WARNING_THRESHOLD` | `0.8` | Warning threshold (0.0-1.0) |
| `MCP_INCLUDE_TOKEN_COUNT` | `false` | Include token count in response metadata |
| `MCP_EXEMPT_TOOLS` | `""` | Comma-separated list of exempt tool patterns (supports wildcards) |

### Example Configurations

```bash
# Conservative settings for limited environments
export MCP_MAX_TOKENS=2000
export MCP_WARNING_THRESHOLD=0.7
export MCP_EXEMPT_TOOLS="stream_*,bulk_*"

# High-performance settings
export MCP_MAX_TOKENS=8000
export MCP_SUMMARY_STYLE=JsonStructure
export MCP_ENABLE_TRUNCATION=true

# Development settings with detailed info
export MCP_INCLUDE_TOKEN_COUNT=true
export MCP_WARNING_THRESHOLD=0.6
```

## Truncation Strategies

### Ellipsis (Default)
Simple truncation with ellipsis at the end. Best for general text responses.

### Paragraphs
Preserves complete paragraphs, truncating at paragraph boundaries. Best for documentation and explanatory text.

### JsonStructure
Intelligently truncates JSON responses while preserving structure. Keeps important fields and truncates large arrays/objects.

### LineBased
Truncates at line boundaries, preserving complete lines. Best for code, logs, and structured text.

## Response Format

When responses are processed, they may include metadata:

```json
{
  "content": "Processed response content...",
  "metadata": {
    "processed": true,
    "toolName": "find_references",
    "truncated": true,
    "originalTokens": 5000,
    "processedTokens": 1200,
    "truncationStyle": "JsonStructure",
    "warning": "Response truncated from 5000 to 1200 tokens"
  }
}
```

## Tool Exemptions

Tools can be exempt from token limiting using patterns:

```bash
# Exempt all streaming tools
export MCP_EXEMPT_TOOLS="stream_*"

# Exempt multiple tool families
export MCP_EXEMPT_TOOLS="stream_*,bulk_*,file_read"

# Exempt specific tools
export MCP_EXEMPT_TOOLS="project_info,file_list"
```

## Implementation Details

The response processing system works by:

1. **Token Estimation**: Estimates tokens using character-based heuristics
2. **Limit Checking**: Compares against configured limits
3. **Exemption Handling**: Checks if tool is exempt from limiting
4. **Truncation**: Applies appropriate truncation strategy if needed
5. **Metadata Addition**: Adds processing information to response
6. **Warning Generation**: Provides warnings for large responses

## Use Cases

### Development Environments
```bash
export MCP_MAX_TOKENS=2000
export MCP_WARNING_THRESHOLD=0.6
export MCP_INCLUDE_TOKEN_COUNT=true
```

### Production Systems
```bash
export MCP_MAX_TOKENS=4000
export MCP_ENABLE_TRUNCATION=true
export MCP_SUMMARY_STYLE=JsonStructure
```

### API Gateways
```bash
export MCP_MAX_TOKENS=1500
export MCP_EXEMPT_TOOLS="stream_*,bulk_*"
export MCP_WARNING_THRESHOLD=0.8
```

This system ensures MCPsharp provides consistent, manageable responses while giving users the flexibility to configure behavior according to their needs.