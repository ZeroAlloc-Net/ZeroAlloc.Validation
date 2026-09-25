; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|--------------------------------------------------------------------------------
ZV0019  | ZeroAlloc.Validation | Error    | Invalid ZeroAllocGeneratedAccessibility value
ZV0020  | ZeroAlloc.Validation | Error    | ValidationAttribute subclass the generator cannot emit
ZV0021  | ZeroAlloc.Validation | Error    | Custom rule value type does not match the property type
ZV0022  | ZeroAlloc.Validation | Warning  | Unknown placeholder in a custom rule message
ZV0023  | ZeroAlloc.Validation | Error    | Custom rule attribute not accessible from the generated validator
ZV0024  | ZeroAlloc.Validation | Error    | Validation attribute applied where the generator does not read it
ZV0025  | ZeroAlloc.Validation | Error    | [Validate] type not accessible from the generated validator
ZV0026  | ZeroAlloc.Validation | Warning  | [RuleMessage] on a class that is not a custom rule
ZV0027  | ZeroAlloc.Validation | Error    | Validation attribute applied to a property the generated validator cannot read
ZV0028  | ZeroAlloc.Validation | Error    | Validation method the generated validator cannot call
ZV0029  | ZeroAlloc.Validation | Error    | [Validate] on a generic type
ZV0030  | ZeroAlloc.Validation | Error    | Validation method call that does not compile
ZV0031  | ZeroAlloc.Validation | Error    | Two [Validate] models whose validators would have the same name
ZV0032  | ZeroAlloc.Validation | Warning  | Validation call that raises a compiler warning in the generated validator
