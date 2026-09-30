namespace ZeroAlloc.Validation.Tests.AspNetCore;

// Not [Validate]: an argument of this type is validated as the closing it derives from, #238.
public class SpecialCrate : Crate<Parcel>
{
}
