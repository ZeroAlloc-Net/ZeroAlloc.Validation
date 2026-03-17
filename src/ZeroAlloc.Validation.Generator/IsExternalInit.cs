// Polyfill required for C# record types when targeting netstandard2.0.
// The compiler emits references to this type for init-only setters used by records.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
