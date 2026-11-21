; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
ZV0035  | ZeroAlloc.Validation | Error | ZeroAlloc.Validation | Warning | [PipelineBehavior] type that does not implement IPipelineBehavior; promoted to an error, ZeroAlloc-Net/.github#48
