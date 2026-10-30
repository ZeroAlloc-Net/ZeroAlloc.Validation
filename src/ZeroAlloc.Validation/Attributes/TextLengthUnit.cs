namespace ZeroAlloc.Validation;

/// <summary>
/// What <see cref="MinLengthAttribute"/>, <see cref="MaxLengthAttribute"/> and
/// <see cref="LengthAttribute"/> count when they measure a string.
/// </summary>
// IMPORTANT: CodeUnits must remain the first (zero-value) member. It is the default of every
// length rule's Unit, and the generator reads an unset Unit as 0.
public enum TextLengthUnit
{
    /// <summary>
    /// UTF-16 code units, what <see cref="string.Length"/> counts. A character outside the Basic
    /// Multilingual Plane, such as an emoji, is two code units. This matches
    /// <c>System.ComponentModel.DataAnnotations</c> and SQL Server's <c>nvarchar(n)</c>.
    /// </summary>
    CodeUnits,

    /// <summary>
    /// Unicode code points: a surrogate pair counts as one, and so does a lone surrogate. This
    /// matches JSON Schema's <c>minLength</c> and <c>maxLength</c>, and PostgreSQL's
    /// <c>varchar(n)</c> in a UTF-8 database. It applies to a <see cref="string"/> only. A
    /// combining sequence such as <c>e</c> followed by U+0301 is still two code points.
    /// </summary>
    CodePoints,
}
