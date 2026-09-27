namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A compiler warning on one call, with the compiler's own ID and message. <paramref name="IsError"/>
/// is set when the project makes it an error, or when it is an error by default, as an
/// <c>[Experimental]</c> API's diagnostic is: ZV0032 is then reported as an error too, so
/// mirroring it never lets a build through that the generated call would have failed.
/// <para>
/// <paramref name="LeavesCallOut"/> is set for CS0619, a use of an <c>[Obsolete(error: true)]</c>
/// member. That is a genuine compiler error, not a warning, and pragma cannot suppress it, so
/// <see cref="CallLineWriter"/> leaves the call's line, and the statement it opens, out of the
/// generated file rather than wrapping it, and ZV0032 reports it as an error instead.
/// </para>
/// </summary>
internal readonly record struct CallWarning(string Id, string Message, bool IsError, bool LeavesCallOut);
