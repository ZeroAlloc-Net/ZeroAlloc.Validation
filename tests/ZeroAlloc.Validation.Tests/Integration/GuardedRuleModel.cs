using System;
using System.Collections.Generic;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Every built-in rule, a <c>[Must]</c> and a custom rule, once behind a <c>When</c> guard and
/// once behind an <c>Unless</c> guard, #282. Several rules emit a condition of more than one term,
/// such as a range's <c>lower || upper</c>, so the guard must cover the whole condition. No nested
/// property, so the validator takes the flat emission path. Every default is valid.
/// </summary>
[Validate]
public class GuardedRuleModel
{
    public static readonly Guid KnownGuid = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    public bool Guarded { get; set; }

    [InclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public int InclusiveBetweenWhen { get; set; } = 5;

    [InclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public int InclusiveBetweenUnless { get; set; } = 5;

    [ExclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public int ExclusiveBetweenWhen { get; set; } = 5;

    [ExclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public int ExclusiveBetweenUnless { get; set; } = 5;

    [InclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public int? NullableInclusiveBetweenWhen { get; set; } = 5;

    [InclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public int? NullableInclusiveBetweenUnless { get; set; } = 5;

    [ExclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public int? NullableExclusiveBetweenWhen { get; set; } = 5;

    [ExclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public int? NullableExclusiveBetweenUnless { get; set; } = 5;

    [GreaterThan(0, When = nameof(IsGuarded))]
    public int GreaterThanWhen { get; set; } = 5;

    [GreaterThan(0, Unless = nameof(IsGuarded))]
    public int GreaterThanUnless { get; set; } = 5;

    [GreaterThan(0, When = nameof(IsGuarded))]
    public int? NullableGreaterThanWhen { get; set; } = 5;

    [GreaterThan(0, Unless = nameof(IsGuarded))]
    public int? NullableGreaterThanUnless { get; set; } = 5;

    [GreaterThanOrEqualTo(1, When = nameof(IsGuarded))]
    public int? NullableGreaterThanOrEqualToWhen { get; set; } = 5;

    [GreaterThanOrEqualTo(1, Unless = nameof(IsGuarded))]
    public int? NullableGreaterThanOrEqualToUnless { get; set; } = 5;

    [LessThan(10, When = nameof(IsGuarded))]
    public int? NullableLessThanWhen { get; set; } = 5;

    [LessThan(10, Unless = nameof(IsGuarded))]
    public int? NullableLessThanUnless { get; set; } = 5;

    [LessThanOrEqualTo(9, When = nameof(IsGuarded))]
    public int? NullableLessThanOrEqualToWhen { get; set; } = 5;

    [LessThanOrEqualTo(9, Unless = nameof(IsGuarded))]
    public int? NullableLessThanOrEqualToUnless { get; set; } = 5;

    [Equal(5, When = nameof(IsGuarded))]
    public int? NullableEqualWhen { get; set; } = 5;

    [Equal(5, Unless = nameof(IsGuarded))]
    public int? NullableEqualUnless { get; set; } = 5;

    [NotEqual(0, When = nameof(IsGuarded))]
    public int? NullableNotEqualWhen { get; set; } = 5;

    [NotEqual(0, Unless = nameof(IsGuarded))]
    public int? NullableNotEqualUnless { get; set; } = 5;

    [InclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public string? StringInclusiveBetweenWhen { get; set; } = "5";

    [InclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public string? StringInclusiveBetweenUnless { get; set; } = "5";

    [ExclusiveBetween(2, 10, When = nameof(IsGuarded))]
    public string? StringExclusiveBetweenWhen { get; set; } = "5";

    [ExclusiveBetween(2, 10, Unless = nameof(IsGuarded))]
    public string? StringExclusiveBetweenUnless { get; set; } = "5";

    [GreaterThan(0, When = nameof(IsGuarded))]
    public string? StringGreaterThanWhen { get; set; } = "5";

    [GreaterThan(0, Unless = nameof(IsGuarded))]
    public string? StringGreaterThanUnless { get; set; } = "5";

    [PrecisionScale(4, 2, When = nameof(IsGuarded))]
    public decimal PrecisionScaleWhen { get; set; } = 1.5m;

    [PrecisionScale(4, 2, Unless = nameof(IsGuarded))]
    public decimal PrecisionScaleUnless { get; set; } = 1.5m;

    [PrecisionScale(4, 2, When = nameof(IsGuarded))]
    public decimal? NullablePrecisionScaleWhen { get; set; } = 1.5m;

    [PrecisionScale(4, 2, Unless = nameof(IsGuarded))]
    public decimal? NullablePrecisionScaleUnless { get; set; } = 1.5m;

    [Length(2, 5, When = nameof(IsGuarded))]
    public string? LengthWhen { get; set; } = "abc";

    [Length(2, 5, Unless = nameof(IsGuarded))]
    public string? LengthUnless { get; set; } = "abc";

    [MinLength(2, When = nameof(IsGuarded))]
    public string? MinLengthWhen { get; set; } = "abc";

    [MinLength(2, Unless = nameof(IsGuarded))]
    public string? MinLengthUnless { get; set; } = "abc";

    [MaxLength(5, When = nameof(IsGuarded))]
    public string? MaxLengthWhen { get; set; } = "abc";

    [MaxLength(5, Unless = nameof(IsGuarded))]
    public string? MaxLengthUnless { get; set; } = "abc";

    [Length(2, 5, When = nameof(IsGuarded))]
    public Username ValueObjectLengthWhen { get; set; } = new("abc");

    [Length(2, 5, Unless = nameof(IsGuarded))]
    public Username ValueObjectLengthUnless { get; set; } = new("abc");

    [Equal("ok", When = nameof(IsGuarded))]
    public string? EqualTextWhen { get; set; } = "ok";

    [Equal("ok", Unless = nameof(IsGuarded))]
    public string? EqualTextUnless { get; set; } = "ok";

    [NotEqual("bad", When = nameof(IsGuarded))]
    public string? NotEqualTextWhen { get; set; } = "ok";

    [NotEqual("bad", Unless = nameof(IsGuarded))]
    public string? NotEqualTextUnless { get; set; } = "ok";

    [IsEnumName(typeof(TrafficLight), When = nameof(IsGuarded))]
    public string? IsEnumNameWhen { get; set; } = "Red";

    [IsEnumName(typeof(TrafficLight), Unless = nameof(IsGuarded))]
    public string? IsEnumNameUnless { get; set; } = "Red";

    [IsInEnum(When = nameof(IsGuarded))]
    public TrafficLight IsInEnumWhen { get; set; } = TrafficLight.Red;

    [IsInEnum(Unless = nameof(IsGuarded))]
    public TrafficLight IsInEnumUnless { get; set; } = TrafficLight.Red;

    [IsInEnum(When = nameof(IsGuarded))]
    public TrafficLight? NullableIsInEnumWhen { get; set; } = TrafficLight.Red;

    [IsInEnum(Unless = nameof(IsGuarded))]
    public TrafficLight? NullableIsInEnumUnless { get; set; } = TrafficLight.Red;

    [Matches("^[0-9]+$", When = nameof(IsGuarded))]
    public string? MatchesWhen { get; set; } = "123";

    [Matches("^[0-9]+$", Unless = nameof(IsGuarded))]
    public string? MatchesUnless { get; set; } = "123";

    [EmailAddress(When = nameof(IsGuarded))]
    public string? EmailAddressWhen { get; set; } = "a@b.com";

    [EmailAddress(Unless = nameof(IsGuarded))]
    public string? EmailAddressUnless { get; set; } = "a@b.com";

    [NotEmpty(When = nameof(IsGuarded))]
    public string? NotEmptyStringWhen { get; set; } = "x";

    [NotEmpty(Unless = nameof(IsGuarded))]
    public string? NotEmptyStringUnless { get; set; } = "x";

    [NotEmpty(When = nameof(IsGuarded))]
    public ICollection<int>? NotEmptyListWhen { get; set; } = [1];

    [NotEmpty(Unless = nameof(IsGuarded))]
    public ICollection<int>? NotEmptyListUnless { get; set; } = [1];

    [NotEmpty(When = nameof(IsGuarded))]
    public int[]? NotEmptyArrayWhen { get; set; } = [1];

    [NotEmpty(Unless = nameof(IsGuarded))]
    public int[]? NotEmptyArrayUnless { get; set; } = [1];

    [NotEmpty(When = nameof(IsGuarded))]
    public Guid? NotEmptyGuidWhen { get; set; } = KnownGuid;

    [NotEmpty(Unless = nameof(IsGuarded))]
    public Guid? NotEmptyGuidUnless { get; set; } = KnownGuid;

    [NotNull(When = nameof(IsGuarded))]
    public string? NotNullWhen { get; set; } = "x";

    [NotNull(Unless = nameof(IsGuarded))]
    public string? NotNullUnless { get; set; } = "x";

    [Null(When = nameof(IsGuarded))]
    public string? NullWhen { get; set; }

    [Null(Unless = nameof(IsGuarded))]
    public string? NullUnless { get; set; }

    [Empty(When = nameof(IsGuarded))]
    public string? EmptyWhen { get; set; } = "";

    [Empty(Unless = nameof(IsGuarded))]
    public string? EmptyUnless { get; set; } = "";

    [Must(nameof(IsEven), When = nameof(IsGuarded))]
    public int MustWhen { get; set; } = 2;

    [Must(nameof(IsEven), Unless = nameof(IsGuarded))]
    public int MustUnless { get; set; } = 2;

    [NotBlank(When = nameof(IsGuarded))]
    public string? NotBlankWhen { get; set; } = "x";

    [NotBlank(Unless = nameof(IsGuarded))]
    public string? NotBlankUnless { get; set; } = "x";

    public bool IsGuarded() => Guarded;

    public bool IsEven(int value) => value % 2 == 0;
}
