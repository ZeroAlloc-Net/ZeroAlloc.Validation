using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// Every comparison-style rule on a nullable property, over ranges on both sides of 0, so a null
/// read as 0 shows up whichever side 0 falls on. All null by default, which must be valid: whether
/// a missing value is acceptable is <c>[NotNull]</c>'s decision. See #276.
/// </summary>
[Validate]
public class NullableComparisonModel
{
    [GreaterThan(0)] public int? GreaterThanInt { get; set; }
    [GreaterThan(0)] public long? GreaterThanLong { get; set; }
    [GreaterThan(0)] public short? GreaterThanShort { get; set; }
    [GreaterThan(0)] public byte? GreaterThanByte { get; set; }
    [GreaterThan(0)] public uint? GreaterThanUInt { get; set; }
    [GreaterThan(0)] public ulong? GreaterThanULong { get; set; }
    [GreaterThan(0)] public float? GreaterThanFloat { get; set; }
    [GreaterThan(0)] public double? GreaterThanDouble { get; set; }
    [GreaterThan(0)] public decimal? GreaterThanDecimal { get; set; }
    [GreaterThan(0)] public TrafficLight? GreaterThanEnum { get; set; }

    [GreaterThanOrEqualTo(1)] public int? GreaterThanOrEqualToInt { get; set; }
    [LessThan(-1)] public long? LessThanLong { get; set; }
    [LessThan(5)] public int? LessThanInt { get; set; }
    [LessThanOrEqualTo(-1)] public double? LessThanOrEqualToDouble { get; set; }
    [InclusiveBetween(1, 10)] public decimal? InclusiveBetweenDecimal { get; set; }
    [ExclusiveBetween(1, 10)] public float? ExclusiveBetweenFloat { get; set; }
    [Equal(5)] public int? EqualInt { get; set; }
    [NotEqual(0)] public int? NotEqualInt { get; set; }
    [Equal("expected")] public string? EqualString { get; set; }
    [GreaterThan(0)] public string? GreaterThanNumericString { get; set; }
    [PrecisionScale(5, 2)] public decimal? PrecisionScaleDecimal { get; set; }
    [IsEnumName(typeof(TrafficLight))] public string? EnumName { get; set; }
}
