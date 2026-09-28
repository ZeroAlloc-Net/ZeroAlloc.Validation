using System;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.AotSmoke;

// Value-type coverage for the generated validators: nullable primitives, a nullable Guid,
// enums and nullable enums, user structs and nullable structs, through built-in rules,
// ValidationAttribute<T> rules and [Must]. Nullable<T> is the shape dotnet/runtime#134799
// hangs on under NativeAOT, which is how ZeroAlloc.Cache#182 shipped past a smoke that only
// drove strings and ints. The defaults below are all valid.
[Validate]
public sealed class Reading
{
    [Positive] public int? Score { get; set; }

    // ValidationAttribute<int?> on an int property: the value converts to int? per call.
    [Positive] public int Count { get; set; } = 1;

    [GreaterThan(0)] public int? Quantity { get; set; } = 3;

    [InclusiveBetween(1, 10)] public decimal? Rating { get; set; } = 5m;

    [NotNull] public long? Sequence { get; set; } = 1;

    [NotEmpty] public Guid? CorrelationId { get; set; } = new Guid(0x11111111, 0x2222, 0x3333, 4, 5, 6, 7, 8, 9, 10, 11);

    [IsInEnum] public SmokeLevel Level { get; set; } = SmokeLevel.Low;

    [IsInEnum] public SmokeLevel? OptionalLevel { get; set; }

    [KnownLevel] public SmokeLevel Tier { get; set; } = SmokeLevel.Medium;

    [OrderedRange] public SmokeRange Window { get; set; } = new(1, 2);

    [OrderedRangeOrNull] public SmokeRange? OptionalWindow { get; set; }

    [Must(nameof(IsEven))] public int? Batch { get; set; }

    public Tolerance Tolerance { get; set; } = new(0.5);

    public bool IsEven(int? value) => value is null || value.Value % 2 == 0;
}
