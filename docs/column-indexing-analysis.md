# MCPsharp File Edit Column Indexing Analysis

## Issue Summary

The `file_edit` tool in MCPsharp is experiencing column indexing inconsistencies that cause errors when applying text edits. Users report two main errors:
- "The given key was not present in the dictionary"
- "Index and length must refer to a location within the string"

## Current Column Indexing System

### 1. Indexing Convention

**MCPsharp uses 0-indexed positions for both lines and columns:**
- Line 0 = First line in file
- Column 0 = First character in line
- Position refers to the character before which the operation occurs

### 2. Edit Position Calculation

```csharp
// Example: Original text "int x = 5;" (line 0)
// Character positions:
// 0 1 2 3 4 5 6 7 8 9 10
// i n t   x =   5 ;
//           ^ ^   ^ ^
//           | |   | |
//   StartColumn=8 |   EndColumn=9
//                 |
//             EndColumn=10 (after semicolon)
```

### 3. String Slicing Implementation

The current implementation uses C# range syntax:

```csharp
// FileOperationsService.cs line 326
var line = lines[edit.StartLine];
var before = line[..edit.StartColumn];  // Characters before edit
var after = line[edit.EndColumn..];     // Characters after edit
lines[edit.StartLine] = before + edit.NewText + after;
```

## Identified Issues

### Issue 1: Off-by-One Errors in EndColumn

**Problem**: Users often expect EndColumn to be inclusive, but the implementation treats it as exclusive.

**Example**:
```csharp
// User wants to replace "5" with "10" in "int x = 5;"
// User might provide: StartColumn=8, EndColumn=9 (expecting inclusive)
// But implementation treats EndColumn=9 as exclusive, only replacing position 8

// Correct positions should be:
StartColumn = 8, EndColumn = 9  // For replacing single character at position 8
```

### Issue 2: Unicode and Multi-byte Characters

**Problem**: Column counting doesn't account for Unicode combining characters.

```csharp
// Text: "café" (where é is a single display character but multiple bytes)
// UTF-8 bytes: [c][a][f][é][́] - 5 bytes
// Visual columns: 0  1  2  3   4
// Byte indices:  0  1  2  3 4 5

// Current implementation counts bytes, not visual columns
```

### Issue 3: Tab Character Handling

**Problem**: Tabs are treated as single characters but may represent multiple visual columns.

```csharp
// Text: "int\tx"
// Visual representation with 4-space tabs: "int    x"
// Byte indices:  0 1 2 3 4
// Visual cols:   0 1 2 3 4 5 6 7 8

// Current: tab = 1 column
// Expected: tab = N columns (based on editor configuration)
```

### Issue 4: Line Ending Inconsistencies

**Problem**: Different line endings affect column calculations.

```csharp
// Windows: \r\n (2 characters)
// Unix: \n (1 character)
// Old Mac: \r (1 character)

// Current implementation splits on \n only
var lines = content.Split('\n').ToList();
```

### Issue 5: Empty Lines and Edge Cases

**Problem**: Edge cases not properly handled.

```csharp
// Empty line: ""
// Any column access should throw an error
// Current code: line[..column] where column > 0 throws exception

// Line shorter than expected column
// Example: line = "abc" (length 3), but edit specifies column 5
```

## Root Cause Analysis

### 1. Inconsistent Indexing Conventions

The MCP tool interface doesn't clearly document whether positions are 0-indexed or 1-indexed, leading to user confusion.

### 2. Missing Validation

The implementation doesn't validate that:
- StartLine and EndLine are within file bounds
- StartColumn and EndColumn are within line bounds
- Start position is before End position

### 3. Character vs Column Confusion

The system mixes character byte indices with visual column positions without proper conversion.

## Proposed Solutions

### Solution 1: Clear Indexing Documentation

```csharp
/// <summary>
/// Starting column (0-indexed). Refers to the character position within the line.
/// Column 0 is before the first character. Column N is between characters N-1 and N.
/// </summary>
public required int StartColumn { get; init; }

/// <summary>
/// Ending column (0-indexed, exclusive). The edit range is [StartColumn, EndColumn).
/// EndColumn is the position after the last character to be edited.
/// </summary>
public required int EndColumn { get; init; }
```

### Solution 2: Enhanced Validation

```csharp
private void ValidateEditPosition(List<string> lines, TextEdit edit)
{
    // Validate line indices
    if (edit.StartLine < 0 || edit.StartLine >= lines.Count)
        throw new ArgumentOutOfRangeException(nameof(edit.StartLine));

    if (edit.EndLine < 0 || edit.EndLine >= lines.Count)
        throw new ArgumentOutOfRangeException(nameof(edit.EndLine));

    // Validate column indices for single line edits
    if (edit.StartLine == edit.EndLine)
    {
        var line = lines[edit.StartLine];
        if (edit.StartColumn < 0 || edit.StartColumn > line.Length)
            throw new ArgumentOutOfRangeException(nameof(edit.StartColumn));

        if (edit.EndColumn < 0 || edit.EndColumn > line.Length)
            throw new ArgumentOutOfRangeException(nameof(edit.EndColumn));

        if (edit.StartColumn > edit.EndColumn)
            throw new ArgumentException("StartColumn cannot be greater than EndColumn");
    }
    else
    {
        // Multi-line validation
        var startLine = lines[edit.StartLine];
        var endLine = lines[edit.EndLine];

        if (edit.StartColumn < 0 || edit.StartColumn > startLine.Length)
            throw new ArgumentOutOfRangeException(nameof(edit.StartColumn));

        if (edit.EndColumn < 0 || edit.EndColumn > endLine.Length)
            throw new ArgumentOutOfRangeException(nameof(edit.EndColumn));
    }
}
```

### Solution 3: Improved Error Messages

```csharp
catch (ArgumentOutOfRangeException ex)
{
    return new FileEditResult
    {
        Success = false,
        Error = $"Invalid edit position: {ex.Message}. " +
                $"File has {lines.Count} lines. " +
                $"Attempted to edit line {edit.StartLine}, columns {edit.StartColumn}-{edit.EndColumn}. " +
                $"Line {edit.StartLine} has {lines[edit.StartLine].Length} characters.",
        Path = relativePath
    };
}
```

### Solution 4: UTF-32 Column Position Tracking

```csharp
private int GetVisualColumnIndex(string line, int byteIndex)
{
    // Convert byte index to Unicode scalar index
    var scalarIndex = line.AsSpan().Slice(0, byteIndex).EnumerateRunes().Count();
    return scalarIndex;
}

private int GetByteIndex(string line, int visualColumn)
{
    // Convert visual column to byte index
    var byteIndex = 0;
    var currentColumn = 0;

    foreach (var rune in line.EnumerateRunes())
    {
        if (currentColumn >= visualColumn)
            break;

        currentColumn++;
        byteIndex += rune.Utf8SequenceLength;
    }

    return byteIndex;
}
```

### Solution 5: Tab Expansion Option

```csharp
public class TextEditOptions
{
    /// <summary>
    /// Number of spaces to expand tabs to for column calculation
    /// Default: 4 (as most editors)
    /// </summary>
    public int TabSize { get; init; } = 4;

    /// <summary>
    /// Whether to use visual columns (accounting for tabs) or character positions
    /// Default: false (use character positions for consistency)
    /// </summary>
    public bool UseVisualColumns { get; init; } = false;
}

private int GetColumnWithTabs(string line, int column, int tabSize)
{
    var visualColumn = 0;
    var charColumn = 0;

    for (int i = 0; i < line.Length && charColumn < column; i++)
    {
        if (line[i] == '\t')
            visualColumn += tabSize - (visualColumn % tabSize);
        else
            visualColumn++;

        charColumn++;
    }

    return visualColumn;
}
```

### Solution 6: Enhanced Edit Method

```csharp
public async Task<FileEditResult> EditFileAsync(
    string relativePath,
    IEnumerable<TextEdit> edits,
    TextEditOptions? options = null,
    CancellationToken ct = default)
{
    options ??= new TextEditOptions();

    // ... existing validation code ...

    var content = await File.ReadAllTextAsync(fullPath, ct);

    // Normalize line endings
    content = content.Replace("\r\n", "\n").Replace('\r', '\n');
    var lines = content.Split('\n').ToList();

    // Validate each edit
    foreach (var edit in edits)
    {
        try
        {
            ValidateEditPosition(lines, edit);
        }
        catch (Exception ex)
        {
            return new FileEditResult
            {
                Success = false,
                Error = $"Invalid edit position for {edit.Type}: {ex.Message}",
                Path = relativePath,
                ValidationError = new EditValidationError
                {
                    Edit = edit,
                    LineCount = lines.Count,
                    LineLengths = lines.Select(l => l.Length).ToArray(),
                    Suggestion = GenerateEditSuggestion(lines, edit, options)
                }
            };
        }
    }

    // Apply edits with enhanced error handling
    var sortedEdits = edits
        .OrderByDescending(e => GetEditStartPosition(e))
        .ToList();

    foreach (var edit in sortedEdits)
    {
        try
        {
            ApplyEditEnhanced(lines, edit, options);
        }
        catch (Exception ex)
        {
            return new FileEditResult
            {
                Success = false,
                Error = $"Failed to apply {edit.Type} edit: {ex.Message}",
                Path = relativePath
            };
        }
    }

    // ... rest of method ...
}
```

## Implementation Priority

1. **High Priority**: Add validation and clear error messages
2. **High Priority**: Document indexing convention clearly
3. **Medium Priority**: Add UTF-32 support for proper Unicode handling
4. **Medium Priority**: Add tab expansion options
5. **Low Priority**: Migrate to visual columns by default

## Testing Strategy

Create comprehensive tests for edge cases:

```csharp
[Test]
public void EditFile_WithEmptyLine_ShouldHandleGracefully()
{
    // Test editing empty lines
}

[Test]
public void EditFile_WithUnicodeCharacters_ShouldUseVisualColumns()
{
    // Test Unicode combining characters
}

[Test]
public void EditFile_WithTabs_ShouldExpandCorrectly()
{
    // Test tab expansion
}

[Test]
public void EditFile_AtLineBoundaries_ShouldNotFail()
{
    // Test edits at start and end of lines
}
```

## Migration Guide

For users transitioning from the old system:

1. **Document the change** clearly in release notes
2. **Provide a migration tool** that converts 1-indexed to 0-indexed positions
3. **Add a compatibility mode** that accepts 1-indexed positions with a warning
4. **Update all examples** in documentation

By implementing these solutions, MCPsharp will provide a more reliable and predictable file editing experience with clear error messages and proper handling of edge cases.