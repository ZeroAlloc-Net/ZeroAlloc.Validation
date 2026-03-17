; Unshipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZV0011 | ZeroAlloc.Validation | Warning | Redundant [ValidateWith] attribute
ZV0012 | ZeroAlloc.Validation | Error | Invalid [ValidateWith] validator type
ZV0013 | ZeroAlloc.Validation | Error | Invalid [CustomValidation] method signature
ZV0015 | ZeroAlloc.Validation | Error | Duplicate pipeline behavior Order
