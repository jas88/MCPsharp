; Unshipped analyzer release
; <https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md>
; Roslyn's release tracking analyzer (RS2007) needs the table rows below exactly as they are, so
; markdownlint must not re-space them: <!-- markdownlint-disable-file MD060 -->

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MCP001 | Async | Warning | AsyncAwaitPatternAnalyzer
MCP002 | Logging | Warning | ExceptionLoggingAnalyzer
MCP003 | CodeQuality | Info | UnusedCodeAnalyzer
MCP004 | CodeQuality | Info | UnusedCodeAnalyzer
MCP005 | Performance | Info | StaticMethodAnalyzer
