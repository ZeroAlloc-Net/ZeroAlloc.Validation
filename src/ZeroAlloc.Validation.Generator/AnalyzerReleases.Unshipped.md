; Unshipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZV0011 | ZeroAlloc.Validation | Warning | Redundant [ValidateWith] attribute
ZV0012 | ZeroAlloc.Validation | Error | Invalid [ValidateWith] validator type
ZV0013 | ZeroAlloc.Validation | Error | Invalid [CustomValidation] method signature
ZV0014 | ZeroAlloc.Validation | Warning | [Validate] on non-readonly struct
ZV0015 | ZeroAlloc.Validation | Error | Duplicate pipeline behavior Order
ZV0016 | ZeroAlloc.Validation | Warning | Multi-property value-object can't be auto-unwrapped
ZV0017 | ZeroAlloc.Validation | Warning | Validation rules depending on an inaccessible base member are ignored
ZV0018 | ZeroAlloc.Validation | Warning | Duplicate validation attribute
