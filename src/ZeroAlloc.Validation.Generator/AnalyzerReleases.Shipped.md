; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.1.2

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|---------------------------------------------
ZV0011  | ZeroAlloc.Validation | Warning  | Redundant [ValidateWith] attribute
ZV0012  | ZeroAlloc.Validation | Error    | Invalid [ValidateWith] validator type
ZV0013  | ZeroAlloc.Validation | Error    | Invalid [CustomValidation] method signature
ZV0015  | ZeroAlloc.Validation | Error    | Duplicate pipeline behavior Order

## Release 1.4.0

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|-----------------------------------
ZV0014  | ZeroAlloc.Validation | Warning  | [Validate] on non-readonly struct

## Release 1.5.0

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|-----------------------------------------------------
ZV0016  | ZeroAlloc.Validation | Warning  | Multi-property value-object can't be auto-unwrapped

## Release 1.5.7

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|-----------------------------------------------------------------------
ZV0017  | ZeroAlloc.Validation | Warning  | Validation rules depending on an inaccessible base member are ignored

## Release 1.7.0

### New Rules

Rule ID | Category             | Severity | Notes
--------|----------------------|----------|--------------------------------
ZV0018  | ZeroAlloc.Validation | Warning  | Duplicate validation attribute
