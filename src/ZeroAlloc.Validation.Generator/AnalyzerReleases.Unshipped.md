; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|--------------------------------------------------------------------------------
ZV0034  | ZeroAlloc.Validation | Error    | Options validation of a model with asynchronous rules
ZV0035  | ZeroAlloc.Validation | Warning  | [PipelineBehavior] type that does not implement IPipelineBehavior
ZV0036  | ZeroAlloc.Validation | Error    | Built-in rule on a value whose type is a type parameter
ZV0037  | ZeroAlloc.Validation | Error    | Nested model that nests its model inside itself without end
ZV0038  | ZeroAlloc.Validation | Warning  | Pipeline behavior applied to a closed form of a generic model
