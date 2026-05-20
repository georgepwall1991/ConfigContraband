; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.3.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CFG002 | Configuration | Warning | Required configuration key is missing

## Release 0.2.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CFG007 | Configuration | Warning | Unknown config key will throw when ErrorOnUnknownConfiguration is enabled

## Release 0.1.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CFG001 | Configuration | Warning | Bound configuration section does not exist
CFG003 | Configuration | Warning | Validation exists but does not run on startup
CFG004 | Configuration | Warning | DataAnnotations exist but are never enabled
CFG005 | Configuration | Warning | Nested options are annotated but not recursively validated
CFG006 | Configuration | Info | Unknown config key under a bound section
