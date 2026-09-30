using System.Buffers;
using System.Globalization;

namespace ZeroAlloc.Validation.Internal;

/// <summary>
/// The failure buffer a generated <c>ValidateAsync</c> collects into. It works as
/// <see cref="FailureBuffer"/> does, but it is not a <c>ref struct</c>, so it can live across an
/// <c>await</c>. Nothing is rented until the first failure, so a valid model neither allocates nor
/// touches the pool.
/// </summary>
public struct AsyncFailureBuffer
{
    private static readonly ArrayPool<ValidationFailure> Pool = ArrayPool<ValidationFailure>.Shared;

    private ValidationFailure[]? _buf;
    private readonly int _capacity;
    private int _count;

    /// <summary>Creates a buffer that rents <paramref name="initialCapacity"/> slots, at least four, on its first failure.</summary>
    public AsyncFailureBuffer(int initialCapacity)
    {
        _capacity = initialCapacity < 4 ? 4 : initialCapacity;
        _buf = null;
        _count = 0;
    }

    /// <summary>The number of failures added so far.</summary>
    public readonly int Count => _count;

    /// <summary>Adds one failure.</summary>
    public void Add(in ValidationFailure f)
    {
        var buf = _buf ??= Pool.Rent(_capacity);
        if (_count == buf.Length)
        {
            var grown = Pool.Rent(buf.Length * 2);
            Array.Copy(buf, grown, _count);
            Pool.Return(buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
            _buf = buf = grown;
        }
        buf[_count++] = f;
    }

    /// <summary>Adds every failure in <paramref name="failures"/>, as a <c>[CustomValidation]</c> method returns them.</summary>
    public void AddRange(ReadOnlySpan<ValidationFailure> failures)
    {
        foreach (ref readonly var f in failures)
            Add(in f);
    }

    /// <summary>
    /// Adds the failures of a nested model's validation, each property name prefixed with
    /// <paramref name="propertyName"/> and a dot, as in <c>Address.Street</c>.
    /// </summary>
    public void AddNested(ValidationResult result, string propertyName)
    {
        foreach (ref readonly var f in result.Failures)
        {
            Add(new ValidationFailure
            {
                PropertyName = propertyName + "." + f.PropertyName,
                ErrorMessage = f.ErrorMessage,
                ErrorCode = f.ErrorCode,
                Severity = f.Severity,
            });
        }
    }

    /// <summary>
    /// Adds the failures of one collection element's validation, each property name prefixed
    /// with <paramref name="propertyName"/> and the element's index, as in <c>Items[2].Sku</c>.
    /// </summary>
    public void AddNested(ValidationResult result, string propertyName, int index)
    {
        if (result.IsValid) return;

        var prefix = propertyName + "[" + index.ToString(CultureInfo.InvariantCulture) + "].";
        foreach (ref readonly var f in result.Failures)
        {
            Add(new ValidationFailure
            {
                PropertyName = prefix + f.PropertyName,
                ErrorMessage = f.ErrorMessage,
                ErrorCode = f.ErrorCode,
                Severity = f.Severity,
            });
        }
    }

    /// <summary>
    /// The result holding every failure added, returning the rented array to the pool. A buffer
    /// without failures returns a result over an empty array, which allocates nothing.
    /// </summary>
    public ValidationResult ToResult()
    {
        var buf = _buf;
        if (buf is null)
            return new ValidationResult(Array.Empty<ValidationFailure>());

        _buf = null;
        if (_count == 0)
        {
            Pool.Return(buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
            return new ValidationResult(Array.Empty<ValidationFailure>());
        }

        var result = new ValidationFailure[_count];
        Array.Copy(buf, result, _count);
        Pool.Return(buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
        return new ValidationResult(result);
    }
}
