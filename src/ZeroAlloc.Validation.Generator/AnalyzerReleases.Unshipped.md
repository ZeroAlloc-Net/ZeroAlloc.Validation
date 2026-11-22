; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
ZV0035  | ZeroAlloc.Validation | Error | ZeroAlloc.Validation | Warning | [PipelineBehavior] type that does not implement IPipelineBehavior; promoted to an error, ZeroAlloc-Net/.github#48

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|--------------------------------------------------------------------------------
ZV0041  | ZeroAlloc.Validation | Error    | Pipeline behavior without a Handle method of the validation shape
