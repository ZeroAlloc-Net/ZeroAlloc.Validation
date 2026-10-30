namespace ZeroAlloc.Validation;

/// <summary>
/// Fails when the value is shorter than <see cref="Min"/>. A <c>null</c> value passes; use
/// <see cref="NotNullAttribute"/> or <see cref="NotEmptyAttribute"/> to require one. The value
/// must have a readable <c>Length</c>, such as a string or an array, or be a collection with a
/// readable <c>Count</c>, such as <c>List&lt;T&gt;</c>, whose items it counts.
/// </summary>
/// <remarks>
/// A string's length is counted in UTF-16 code units by default, what <see cref="string.Length"/>
/// counts, so an emoji counts as two. Set <see cref="Unit"/> to
/// <see cref="TextLengthUnit.CodePoints"/> to count it as one.
/// </remarks>
public sealed class MinLengthAttribute(int min) : ValidationAttribute
{
    /// <summary>The smallest length that passes.</summary>
    public int Min { get; } = min;

    /// <summary>
    /// What the length of a string is counted in: <see cref="TextLengthUnit.CodeUnits"/>, the
    /// default, or <see cref="TextLengthUnit.CodePoints"/>, which only a string can use.
    /// </summary>
    public TextLengthUnit Unit { get; set; }
}
