using System.Buffers;

namespace ZeroAlloc.Validation.Internal;

public ref struct FailureBuffer
{
    private static readonly ArrayPool<global::ZeroAlloc.Validation.ValidationFailure> Pool =
        ArrayPool<global::ZeroAlloc.Validation.ValidationFailure>.Shared;

    private global::ZeroAlloc.Validation.ValidationFailure[] _buf;
    private int _count;

    // initialCapacity = totalDirectRules; nested/collection failures may exceed this and trigger Grow()
    public FailureBuffer(int initialCapacity)
    {
        _buf = Pool.Rent(initialCapacity < 4 ? 4 : initialCapacity);
        _count = 0;
    }

    public int Count => _count;

    public void Add(in global::ZeroAlloc.Validation.ValidationFailure f)
    {
        if (_count == _buf.Length) Grow();
        _buf[_count++] = f;
    }

    private void Grow()
    {
        var newBuf = Pool.Rent(_buf.Length * 2);
        System.Array.Copy(_buf, newBuf, _count);
        Pool.Return(_buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
        _buf = newBuf;
    }

    public global::ZeroAlloc.Validation.ValidationResult ToResult()
    {
        if (_count == 0)
        {
            Pool.Return(_buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
            _buf = System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>();
            return new global::ZeroAlloc.Validation.ValidationResult(
                System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>());
        }
        var result = new global::ZeroAlloc.Validation.ValidationFailure[_count];
        System.Array.Copy(_buf, result, _count);
        Pool.Return(_buf, clearArray: false); // ValidationFailure is a readonly struct; stale slots pose no GC risk
        _buf = System.Array.Empty<global::ZeroAlloc.Validation.ValidationFailure>();
        return new global::ZeroAlloc.Validation.ValidationResult(result);
    }
}
