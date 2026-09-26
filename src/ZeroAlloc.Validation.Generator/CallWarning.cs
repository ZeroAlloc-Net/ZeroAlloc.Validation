namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A compiler warning on one call, with the compiler's own ID and message. <paramref name="IsError"/>
/// is set when the project makes it an error, or when it is an error by default, as an
/// <c>[Experimental]</c> API's diagnostic is: ZV0032 is then reported as an error too, so
/// mirroring it never lets a build through that the generated call would have failed.
/// </summary>
internal readonly record struct CallWarning(string Id, string Message, bool IsError);
