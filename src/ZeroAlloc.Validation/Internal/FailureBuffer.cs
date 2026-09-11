using System.Buffers;

namespace ZeroAlloc.Validation.Internal;

public ref struct FailureBuffer
{
    private static readonly ArrayPool<global::ZeroAlloc.Validation.ValidationFailure> Pool =
        ArrayPool<global::ZeroAlloc.Validation.ValidationFailure>.Shared;

    private global::ZeroAlloc.Validation.ValidationFailure[]? _buf;
    private readonly int _capacity;
    private int _count;

    // initialCapacity = totalDirectRules; nested/collection failures may exceed this and trigger Grow().
    // Nothing is rented until the first Add, so a validation that reports no failures never touches
    // the pool at all — the valid path stays free of both allocation and rent/return traffic.
    public FailureBuffer(int initialCapacity)
    {
        _capacity = initialCapacity < 4 ? 4 : initialCapacity;
        _buf = null;
        _count = 0;
    }

    public int Count => _count;

    public void Add(in global::ZeroAlloc.Validation.ValidationFailure f)
    {
        var buf = _buf ??= Pool.Rent(_capacity);
        if (_count == buf.Length)
        {
            Grow();
            buf = _buf!;
        }
        buf[_count++] = f;
    }

    private void Grow()
    {
        var current = _buf!;
        var newBuf = Pool.Rent(current.Length * 2);
        System.Array.Copy(current, newBuf, _count);
        Pool.Return(current, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
        _buf = newBuf;
    }

    public global::ZeroAlloc.Validation.ValidationResult ToResult()
    {
        var buf = _buf;
        if (buf is null)
        {
            return new global::ZeroAlloc.Validation.ValidationResult(
                System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());
        }

        if (_count == 0)
        {
            Pool.Return(buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
            _buf = null;
            return new global::ZeroAlloc.Validation.ValidationResult(
                System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());
        }

        var result = new global::ZeroAlloc.Validation.ValidationFailure[_count];
        System.Array.Copy(buf, result, _count);
        Pool.Return(buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
        _buf = null;
        return new global::ZeroAlloc.Validation.ValidationResult(result);
    }
}
