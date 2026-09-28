using System;
using ZeroAlloc.Validation;
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
    if (f.PropertyName.Contains("Postcode", System.StringComparison.Ordinal))
        letterHasPostcodePropertyName = true;
}
if (letterFailureCount != 1 || !letterHasPostcodePropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Letter expected 1 failure with Postcode in PropertyName, got {letterFailureCount} failures (PostcodeMatch={letterHasPostcodePropertyName})");
    foreach (ref readonly var f in emptyLetter.Failures)
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
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
// PropertyName is the load-bearing invariant. The constructor takes ValidatorFor<OrderItem>
// and ValidatorFor<OrderTag>; the generated validators convert to them.
var orderValidator = new OrderValidator(new OrderItemValidator(), new OrderTagValidator());

// Invalid: one valid item at Items[0], one invalid item at Items[1],
// plus one invalid tag at Tags[0]. The Tags[0] case is B3 regression
// coverage — IReadOnlyList<OrderTag> where OrderTag is a [Validate]
// readonly record struct. If the generator's NeedsNullGuard predicate
// regresses, this build fails with CS0037 before this assertion ever runs.
var mixedOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[]
    {
        new OrderItem { Sku = "SKU-1" },
        new OrderItem { Sku = "" }, // Items[1] — invalid
    },
    Tags = new[]
    {
        new OrderTag(Label: ""), // Tags[0] — invalid
    },
});
if (mixedOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — Order with one invalid item + one invalid tag should be invalid");
    return 1;
}

int mixedFailureCount = 0;
bool mixedHasIndexedItemPropertyName = false;
bool mixedHasIndexedTagPropertyName = false;
foreach (ref readonly var f in mixedOrder.Failures)
{
    mixedFailureCount++;
    if (f.PropertyName.Contains("Items[1]", System.StringComparison.Ordinal))
        mixedHasIndexedItemPropertyName = true;
    if (f.PropertyName.Contains("Tags[0]", System.StringComparison.Ordinal))
        mixedHasIndexedTagPropertyName = true;
}
if (mixedFailureCount != 2 || !mixedHasIndexedItemPropertyName || !mixedHasIndexedTagPropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — Order expected 2 failures with 'Items[1]' + 'Tags[0]' in PropertyName, got {mixedFailureCount} failures (ItemsMatch={mixedHasIndexedItemPropertyName}, TagsMatch={mixedHasIndexedTagPropertyName})");
    foreach (ref readonly var f in mixedOrder.Failures)
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
    return 1;
}

// Valid: all items + all tags valid + valid CustomerName.
var validOrder = orderValidator.Validate(new Order
{
    CustomerName = "Alice",
    Items = new[] { new OrderItem { Sku = "SKU-1" } },
    Tags = new[] { new OrderTag(Label: "priority") },
});
if (!validOrder.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — fully-valid Order should be valid");
    return 1;
}

// Fixture 3: Cross-property [Must] predicate.
// Generator emits a direct call to the instance method IsAfterStart(endValue),
// which sees this.StartDate via implicit capture — that's the cross-property
// semantic. PropertyName should be exactly "EndDate" (no method-name suffix).
var dateRangeValidator = new DateRangeValidator();
var anchor = new DateTime(2026, 1, 1);

// Invalid: EndDate before StartDate → 1 failure on EndDate.
var badRange = dateRangeValidator.Validate(new DateRange
{
    StartDate = anchor.AddDays(1),
    EndDate = anchor,
});
if (badRange.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — DateRange with EndDate before StartDate should be invalid");
    return 1;
}

int dateRangeFailureCount = 0;
bool dateRangeHasEndDatePropertyName = false;
foreach (ref readonly var f in badRange.Failures)
{
    dateRangeFailureCount++;
    if (string.Equals(f.PropertyName, "EndDate", StringComparison.Ordinal))
        dateRangeHasEndDatePropertyName = true;
}
if (dateRangeFailureCount != 1 || !dateRangeHasEndDatePropertyName)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — DateRange expected 1 failure with PropertyName=='EndDate', got {dateRangeFailureCount} failures (EndDateMatch={dateRangeHasEndDatePropertyName})");
    foreach (ref readonly var f in badRange.Failures)
        Console.Error.WriteLine($"  failure: PropertyName='{f.PropertyName}', ErrorMessage='{f.ErrorMessage}'");
    return 1;
}

// Valid: EndDate after StartDate → 0 failures.
var goodRange = dateRangeValidator.Validate(new DateRange
{
    StartDate = anchor,
    EndDate = anchor.AddDays(1),
});
if (!goodRange.IsValid)
{
    Console.Error.WriteLine("AOT smoke: FAIL — DateRange with EndDate after StartDate should be valid");
    return 1;
}

// Fixture 4: value types. Nullable primitives, a nullable Guid, enums and nullable enums, user
// structs and nullable structs, through built-in rules, ValidationAttribute<T> rules, [Must] and
// a nested [Validate] struct. Each case asserts the exact failures: property, message and code.
var readingValidator = new ReadingValidator(new ToleranceValidator());

if (Expect(readingValidator.Validate(new Reading()), "Reading defaults") is { } e0) return Fail(e0);

// ValidationAttribute<int?> on an int? property: null passes, and {PropertyValue} formats the value.
if (Expect(readingValidator.Validate(new Reading { Score = null }), "Score null") is { } e1) return Fail(e1);
if (Expect(readingValidator.Validate(new Reading { Score = 4 }), "Score 4") is { } e2) return Fail(e2);
if (Expect(readingValidator.Validate(new Reading { Score = -2 }), "Score -2",
        ("Score", "Score must be positive, got -2.", "POSITIVE")) is { } e3) return Fail(e3);

// The same int? rule on an int property.
if (Expect(readingValidator.Validate(new Reading { Count = 0 }), "Count 0",
        ("Count", "Count must be positive, got 0.", "POSITIVE")) is { } e4) return Fail(e4);

// Built-in comparisons over int? and decimal?. A null passes: the rules check a value that is
// present, and rejecting null is [NotNull]'s job. They used to read null as 0, so
// [GreaterThan(0)] rejected it, see ZeroAlloc-Net/ZeroAlloc.Validation#276.
if (Expect(readingValidator.Validate(new Reading { Quantity = null }), "Quantity null") is { } e5a) return Fail(e5a);
if (Expect(readingValidator.Validate(new Reading { Rating = null }), "Rating null") is { } e5b) return Fail(e5b);
if (Expect(readingValidator.Validate(new Reading { Quantity = 1 }), "Quantity 1") is { } e5) return Fail(e5);
if (Expect(readingValidator.Validate(new Reading { Quantity = 0 }), "Quantity 0",
        ("Quantity", "Quantity must be greater than 0.", null)) is { } e6) return Fail(e6);
if (Expect(readingValidator.Validate(new Reading { Rating = 10m }), "Rating 10") is { } e7) return Fail(e7);
if (Expect(readingValidator.Validate(new Reading { Rating = 10.5m }), "Rating 10.5",
        ("Rating", "Rating must be between 1 and 10.", null)) is { } e8) return Fail(e8);

// [NotNull] on long? and [NotEmpty] on Guid?.
if (Expect(readingValidator.Validate(new Reading { Sequence = null }), "Sequence null",
        ("Sequence", "Sequence must not be null.", null)) is { } e9) return Fail(e9);
if (Expect(readingValidator.Validate(new Reading { CorrelationId = null }), "CorrelationId null",
        ("CorrelationId", "CorrelationId must not be empty.", null)) is { } e10) return Fail(e10);
if (Expect(readingValidator.Validate(new Reading { CorrelationId = Guid.Empty }), "CorrelationId empty",
        ("CorrelationId", "CorrelationId must not be empty.", null)) is { } e11) return Fail(e11);

// [IsInEnum] on an enum and a nullable enum; null is [NotNull]'s decision, so it passes.
if (Expect(readingValidator.Validate(new Reading { Level = (SmokeLevel)9 }), "Level 9",
        ("Level", "Level is not a valid value.", null)) is { } e12) return Fail(e12);
if (Expect(readingValidator.Validate(new Reading { OptionalLevel = null }), "OptionalLevel null") is { } e13) return Fail(e13);
if (Expect(readingValidator.Validate(new Reading { OptionalLevel = SmokeLevel.High }), "OptionalLevel High") is { } e14) return Fail(e14);
if (Expect(readingValidator.Validate(new Reading { OptionalLevel = (SmokeLevel)0 }), "OptionalLevel 0",
        ("OptionalLevel", "OptionalLevel is not a valid value.", null)) is { } e15) return Fail(e15);

// ValidationAttribute<T> over an enum, a struct and a nullable struct.
if (Expect(readingValidator.Validate(new Reading { Tier = (SmokeLevel)7 }), "Tier 7",
        ("Tier", "Tier must be a known level.", null)) is { } e16) return Fail(e16);
if (Expect(readingValidator.Validate(new Reading { Window = new(5, 1) }), "Window (5, 1)",
        ("Window", "Window must have Min <= Max.", null)) is { } e17) return Fail(e17);
if (Expect(readingValidator.Validate(new Reading { OptionalWindow = new SmokeRange(1, 3) }), "OptionalWindow (1, 3)") is { } e18) return Fail(e18);
if (Expect(readingValidator.Validate(new Reading { OptionalWindow = new SmokeRange(4, 2) }), "OptionalWindow (4, 2)",
        ("OptionalWindow", "OptionalWindow must be null or have Min <= Max.", null)) is { } e19) return Fail(e19);

// [Must] over int?.
if (Expect(readingValidator.Validate(new Reading { Batch = 4 }), "Batch 4") is { } e20) return Fail(e20);
if (Expect(readingValidator.Validate(new Reading { Batch = 3 }), "Batch 3",
        ("Batch", "Batch is invalid.", null)) is { } e21) return Fail(e21);

// A nested [Validate] struct as a plain property.
if (Expect(readingValidator.Validate(new Reading { Tolerance = new(1.5) }), "Tolerance 1.5",
        ("Tolerance.Ratio", "Ratio must be between 0 and 1.", null)) is { } e22) return Fail(e22);

// Everything invalid at once, in declaration order.
var allBad = readingValidator.Validate(new Reading
{
    Score = -1,
    Count = -3,
    Quantity = -4,
    Rating = 0.5m,
    Sequence = null,
    CorrelationId = Guid.Empty,
    Level = (SmokeLevel)0,
    OptionalLevel = (SmokeLevel)4,
    Tier = (SmokeLevel)8,
    Window = new(2, 1),
    OptionalWindow = new SmokeRange(9, 8),
    Batch = 1,
    Tolerance = new(-0.1),
});
if (Expect(allBad, "Reading all invalid",
        ("Score", "Score must be positive, got -1.", "POSITIVE"),
        ("Count", "Count must be positive, got -3.", "POSITIVE"),
        ("Quantity", "Quantity must be greater than 0.", null),
        ("Rating", "Rating must be between 1 and 10.", null),
        ("Sequence", "Sequence must not be null.", null),
        ("CorrelationId", "CorrelationId must not be empty.", null),
        ("Level", "Level is not a valid value.", null),
        ("OptionalLevel", "OptionalLevel is not a valid value.", null),
        ("Tier", "Tier must be a known level.", null),
        ("Window", "Window must have Min <= Max.", null),
        ("OptionalWindow", "OptionalWindow must be null or have Min <= Max.", null),
        ("Batch", "Batch is invalid.", null),
        ("Tolerance.Ratio", "Ratio must be between 0 and 1.", null)) is { } e23) return Fail(e23);

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}

// Returns null when result holds exactly the expected failures, in order, else a description.
static string? Expect(ValidationResult result, string label, params (string Property, string Message, string? Code)[] expected)
{
    var failures = result.Failures;
    if (result.IsValid != (expected.Length == 0) || failures.Length != expected.Length)
        return $"{label}: expected {expected.Length} failures, got {failures.Length}: {Describe(failures)}";

    for (var i = 0; i < expected.Length; i++)
    {
        var f = failures[i];
        if (!string.Equals(f.PropertyName, expected[i].Property, StringComparison.Ordinal)
            || !string.Equals(f.ErrorMessage, expected[i].Message, StringComparison.Ordinal)
            || !string.Equals(f.ErrorCode, expected[i].Code, StringComparison.Ordinal))
        {
            return $"{label}: failure {i} expected {expected[i].Property} '{expected[i].Message}' "
                + $"code {expected[i].Code ?? "null"}, got {Describe(failures)}";
        }
    }

    return null;
}

static string Describe(ReadOnlySpan<ValidationFailure> failures)
{
    var text = new System.Text.StringBuilder();
    foreach (ref readonly var f in failures)
        text.Append('[').Append(f.PropertyName).Append(" '").Append(f.ErrorMessage)
            .Append("' code ").Append(f.ErrorCode ?? "null").Append(']');
    return text.Length == 0 ? "none" : text.ToString();
}
