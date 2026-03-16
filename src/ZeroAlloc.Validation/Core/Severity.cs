namespace ZeroAlloc.Validation;

// IMPORTANT: Error must remain the first (zero-value) member.
// The source generator omits Severity from ValidationFailure initializers when
// the value is Error, relying on the struct default (0). Reordering this enum
// would silently break generated validators.
public enum Severity { Error, Warning, Info }
