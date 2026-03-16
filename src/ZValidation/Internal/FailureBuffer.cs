using System.Buffers;

namespace ZValidationInternal;

public ref struct FailureBuffer
{
    private static readonly ArrayPool<global::ZValidation.ValidationFailure> Pool =
        ArrayPool<global::ZValidation.ValidationFailure>.Shared;

    private global::ZValidation.ValidationFailure[] _buf;
    private int _count;

    public FailureBuffer(int initialCapacity)
    {
        _buf = Pool.Rent(initialCapacity < 4 ? 4 : initialCapacity);
        _count = 0;
    }

    public int Count => _count;

    public void Add(in global::ZValidation.ValidationFailure f)
    {
        if (_count == _buf.Length) Grow();
        _buf[_count++] = f;
    }

    private void Grow()
    {
        var newBuf = Pool.Rent(_buf.Length * 2);
        System.Array.Copy(_buf, newBuf, _count);
        Pool.Return(_buf, clearArray: false);
        _buf = newBuf;
    }

    public global::ZValidation.ValidationResult ToResult()
    {
        if (_count == 0)
        {
            Pool.Return(_buf, clearArray: false);
            _buf = System.Array.Empty<global::ZValidation.ValidationFailure>();
            return new global::ZValidation.ValidationResult(
                System.Array.Empty<global::ZValidation.ValidationFailure>());
        }
        var result = new global::ZValidation.ValidationFailure[_count];
        System.Array.Copy(_buf, result, _count);
        Pool.Return(_buf, clearArray: false);
        _buf = System.Array.Empty<global::ZValidation.ValidationFailure>();
        return new global::ZValidation.ValidationResult(result);
    }
}
