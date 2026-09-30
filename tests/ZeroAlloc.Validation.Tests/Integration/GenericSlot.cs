using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>
/// An unconstrained generic model, issue #238, closed over a nullable reference type as well as
/// a value type: its rules work for either.
/// </summary>
[Validate]
public class GenericSlot<T>
{
    [NotNull]
    public T? Value { get; set; }

    [Must(nameof(IsAllowed))]
    public T? Checked { get; set; }

    [NotDefault]
    public T? Marker { get; set; }

    [Tagged(Tag = "slot")]
    public string? Label { get; set; }

    public bool IsAllowed(T? value) => value is null || !Equals(value, Forbidden);

    public T? Forbidden { get; set; }
}
