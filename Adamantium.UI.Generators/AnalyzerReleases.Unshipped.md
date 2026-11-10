; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AWV001 | WebView | Error | A page calls an object's methods alone
AWV002 | WebView | Error | A page names one method by one name
AWV003 | WebView | Error | A page passes values, not references or cancellation tokens
AWV004 | WebView | Error | A page names no type arguments
AWV005 | WebView | Error | An interface a page calls is declared in a namespace
