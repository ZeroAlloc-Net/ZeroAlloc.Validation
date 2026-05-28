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

// Fixture 1: [ValidateWith] pointing at an external ValidatorFor<T>.
// Generator emits a ctor that takes the external ValidatorFor<T> via DI,
// then calls into it at the [ValidateWith] site. Failure flows back into
// the parent Letter's failures.
var letterValidator = new LetterValidator(new PostcodeValidator());

// Invalid: empty Postcode.Value → 1 failure routed through PostcodeValidator.
var emptyLetter = letterValidator.Validate(new Letter { Postcode = new() });
if (emptyLetter.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Letter with empty Postcode should be invalid");
    return 1;
}

int letterFailureCount = 0;
bool letterHasPostcodePropertyName = false;
foreach (ref readonly var f in emptyLetter.Failures)
{
    letterFailureCount++;
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
    if (f.PropertyName.Contains("Postcode", System.StringComparison.Ordinal))
#pragma warning restore EPS06
        letterHasPostcodePropertyName = true;
}
if (letterFailureCount != 1 || !letterHasPostcodePropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Letter expected 1 failure with Postcode in PropertyName, got {letterFailureCount} failures (PostcodeMatch={letterHasPostcodePropertyName})");
    foreach (ref readonly var f in emptyLetter.Failures)
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
#pragma warning restore EPS06
    return 1;
}

// Valid: populated Postcode.Value → 0 failures.
var validLetter = letterValidator.Validate(new Letter { Postcode = new() { Value = "1234 AB" } });
if (!validLetter.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Letter with populated Postcode should be valid");
    return 1;
}

// Fixture 2: Nested IReadOnlyList<[Validate] OrderItem> with per-item indexing.
// Generator emits a foreach over Items, validating each via OrderItemValidator
// and emitting failures with PropertyName "Items[N].Sku" — the indexed
// PropertyName is the load-bearing invariant.
var orderValidator = new OrderValidator(new OrderItemValidator());

// Invalid: one valid item at [0], one invalid item at [1].
var mixedOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[]
    {
        new OrderItem { Sku = "SKU-1" },
        new OrderItem { Sku = "" }, // index 1 — invalid
    },
});
if (mixedOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Order with one invalid item should be invalid");
    return 1;
}

int mixedFailureCount = 0;
bool mixedHasIndexedPropertyName = false;
foreach (ref readonly var f in mixedOrder.Failures)
{
    mixedFailureCount++;
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
    if (f.PropertyName.Contains("Items[1]", System.StringComparison.Ordinal))
#pragma warning restore EPS06
        mixedHasIndexedPropertyName = true;
}
if (mixedFailureCount != 1 || !mixedHasIndexedPropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Order expected 1 failure with 'Items[1]' in PropertyName, got {mixedFailureCount} failures (IndexedMatch={mixedHasIndexedPropertyName})");
    foreach (ref readonly var f in mixedOrder.Failures)
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
#pragma warning restore EPS06
    return 1;
}

// Valid: all items valid + valid CustomerName.
var validOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[] { new OrderItem { Sku = "SKU-1" } },
});
if (!validOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — fully-valid Order should be valid");
    return 1;
}

Console.WriteLine("AOT smoke: PASS");
return 0;
