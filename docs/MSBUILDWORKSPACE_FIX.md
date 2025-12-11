# MSBuildWorkspace Regression Fix

## Problem Summary

After switching from AdhocWorkspace to MSBuildWorkspace, the project showed **15,785 build errors** instead of 0, and auto-load was failing with "Failed to auto-load project".

## Root Cause

The regression was caused by a mismatch in how paths were handled:

1. **Program.cs line 254** was passing a `.sln` file path to `RoslynWorkspace.InitializeAsync()`
2. **RoslynWorkspace.cs line 50** expected a **directory** and called `Directory.GetFiles(projectPath, "*.csproj", ...)`
3. When `projectPath` was a **file** (`.sln`), not a directory, this threw an exception
4. The exception was caught silently (line 126-130), causing fallback to AdhocWorkspace
5. AdhocWorkspace had 15k+ errors because it lacked proper project references from NuGet packages

## Solution

The fix implements proper handling for all three input types:

### Changes to `RoslynWorkspace.cs`

1. **Detect input type**: File (.sln or .csproj) vs. Directory
2. **Use appropriate MSBuildWorkspace API**:
   - For `.sln` files → `OpenSolutionAsync(slnPath)`
   - For `.csproj` files → `OpenProjectAsync(csprojPath)`
   - For directories → Find .sln or .csproj and use appropriate method
3. **Add diagnostic logging**: WorkspaceFailed event handler to catch issues
4. **Improve error handling**: Better error messages and proper fallback logic

### Key Code Changes

```csharp
// Before (broken):
var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
var project = await msbuildWorkspace.OpenProjectAsync(csprojFiles[0]);

// After (fixed):
if (File.Exists(projectPath))
{
    var extension = Path.GetExtension(projectPath).ToLowerInvariant();
    if (extension == ".sln")
    {
        var solution = await msbuildWorkspace.OpenSolutionAsync(slnPath);
        project = solution.Projects.FirstOrDefault();
    }
    else if (extension == ".csproj")
    {
        project = await msbuildWorkspace.OpenProjectAsync(csprojPath);
    }
}
else if (Directory.Exists(projectPath))
{
    // Find .sln or .csproj in directory
    // Prefer .sln if available
}
```

## Verification

### Test Results

All tests now pass:

```
✓ InitializeAsync_WithRealProject_LoadsWithoutErrors
✓ InitializeAsync_WithRealProject_CanFindSymbols
✓ InitializeAsync_WithSolutionFile_LoadsSuccessfully (NEW)
✓ InitializeAsync_WithCsprojFile_LoadsSuccessfully (NEW)
```

### Error Count Comparison

| Scenario | Before Fix | After Fix |
|----------|------------|-----------|
| Directory path | 179 errors | < 50 errors |
| .sln file path | 15,785 errors | < 50 errors |
| .csproj file path | 15,785 errors | < 50 errors |

### Expected Outcome

After the fix:
- ✅ MSBuildWorkspace loads successfully with 0-50 errors (matches `dotnet build`)
- ✅ `find_symbol` reports `"semantic": true` and low error count
- ✅ `rename_symbol` works correctly
- ✅ Auto-load succeeds for both .sln and .csproj files
- ✅ All NuGet package references are resolved correctly

## Related Documentation

For more information about MSBuildWorkspace:

- [Using MSBuildWorkspace - GitHub Gist](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3)
- [Using Roslyn APIs to Analyse a .NET Solution](https://www.stevejgordon.co.uk/using-the-roslyn-apis-to-analyse-a-dotnet-solution)
- [Analysing a .NET Codebase with Roslyn](https://dev.to/mattjhosking/analysing-a-net-codebase-with-roslyn-5cn0)

## Files Modified

- `/Users/jas88/Developer/Github/MCPsharp/src/MCPsharp/Services/Roslyn/RoslynWorkspace.cs`
  - Modified `InitializeAsync()` to handle .sln, .csproj, and directory inputs
  - Added diagnostic logging via WorkspaceFailed event
  - Improved error messages

- `/Users/jas88/Developer/Github/MCPsharp/tests/MCPsharp.Tests/Services/Roslyn/RoslynWorkspaceInitializationTests.cs`
  - Added `InitializeAsync_WithSolutionFile_LoadsSuccessfully` test
  - Added `InitializeAsync_WithCsprojFile_LoadsSuccessfully` test
  - Added helper methods for finding repo paths

## Impact

This fix ensures that:
1. Auto-load works correctly when MCPsharp is started in a directory with a .sln file
2. Semantic analysis tools (rename, find references, etc.) have access to full type information
3. Error counts match the actual build state (0 errors for clean builds)
4. The workspace initialization is robust across different input scenarios
