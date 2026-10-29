namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>Every comparison rule over double, float and their nullable forms, valid by default. See #360.</summary>
[Validate]
public class NanComparisonModel
{
    [GreaterThan(0)] public double GreaterThanDouble { get; set; } = 1;
    [GreaterThanOrEqualTo(0)] public double GreaterThanOrEqualToDouble { get; set; } = 1;
    [LessThan(10)] public double LessThanDouble { get; set; } = 1;
    [LessThanOrEqualTo(10)] public double LessThanOrEqualToDouble { get; set; } = 1;
    [InclusiveBetween(0, 1)] public double InclusiveBetweenDouble { get; set; } = 0.5;
    [ExclusiveBetween(0, 1)] public float ExclusiveBetweenFloat { get; set; } = 0.5f;
    [InclusiveBetween(0, 1)] public double? InclusiveBetweenNullable { get; set; }
    [GreaterThan(0)] public float? GreaterThanNullableFloat { get; set; }
}
