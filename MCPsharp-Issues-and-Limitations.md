# MCPsharp Issues & Limitations Tracker

This document tracks issues, limitations, and failures encountered while using MCPsharp to edit itself during consolidation work.

## 📅 Current Session: 2025-10-30

### Phase 1: File Operations Consolidation

---

## Issue Template
```
### [Date] [Phase]
**Operation:** [What was attempted]
**Tool Used:** [Which MCPsharp tool]
**Error/Issue:** [Description]
**Workaround:** [If any]
**Impact:** [How it affected the work]
```

---

## Summary Statistics
- Total Issues: 2
- Critical Blockers: 1
- Workarounds Found: 1
- Tools with Known Issues: 1

## Issues

### 2025-10-30 Phase 1 - Issue 1 - RESOLVED
**Operation:** Reading McpToolRegistry.cs (47,561 tokens)
**Tool Used:** mcp__MCPsharp__file_read
**Error/Issue:** File content truncated from 47,561 to 1,037 tokens due to size limits
**Workaround:** Used mcp__MCPsharp__file_edit for targeted edits instead of reading entire file
**Impact:** MCPsharp's edit facilities work perfectly for large files - no need to read entire file first

### 2025-10-30 Phase 1 - Issue 2 - INVESTIGATING
**Operation:** Attempting to use mcp__MCPsharp__file_edit for method replacement
**Tool Used:** mcp__MCPsharp__file_edit
**Error/Issue:** Multiple errors: "The given key was not present in the dictionary" and "Index and length must refer to a location within the string"
**Workaround:** Column indexing appears to be inconsistent - need to understand 1-indexed vs 0-indexed conventions
**Impact:** Edit facility has line/column number accuracy issues for precise method replacement

## Categories
- **Tool Execution Failures**
- **API Limitations**
- **Schema/Validation Issues**
- **Build/Compilation Problems**
- **Performance Issues**
- **Documentation Discrepancies**