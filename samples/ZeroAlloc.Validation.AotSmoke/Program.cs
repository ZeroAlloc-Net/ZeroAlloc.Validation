using System;
using ZeroAlloc.Validation.AotSmoke;

// Exercise the generator-emitted AddressValidator under PublishAot=true.
// The generator emits a ValidatorFor<Address> subclass that evaluates every
// [NotEmpty]/etc. attribute at compile time — no reflection.

var validator = new AddressValidator();

// Invalid input: both fields empty → 2 failures expected.
var empty = validator.Validate(new Address());
if (empty.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — empty Address should be invalid");
    return 1;
}

var emptyFailures = System.Linq.Enumerable.Count(empty.Failures.ToArray());
if (emptyFailures != 2)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — empty Address expected 2 failures, got {emptyFailures}");
    return 1;
}

// Valid input: both fields populated → no failures.
var ok = validator.Validate(new Address { Street = "Main St 1", City = "Amsterdam" });
if (!ok.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — fully-populated Address should be valid");
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
