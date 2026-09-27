using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.Validation.Generator.Shared;

/// <summary>
/// Element-wise equatable wrapper around <see cref="ImmutableArray{T}"/>, for the values an
/// incremental generator passes between pipeline stages. <see cref="ImmutableArray{T}"/>, a
/// <see cref="List{T}"/> or an array compare by reference, so a model holding one never compares
/// equal to the one built from the next compilation, and every downstream step reruns, issue #209.
/// </summary>
/// <remarks>
/// Taken from ZeroAlloc.ORM's generator. Elements are compared with
/// <see cref="EqualityComparer{T}.Default"/>, which also accepts <see langword="null"/> elements.
/// Shared as source into every generator in this repository, like the other files here.
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values;

    public EquatableArray(ImmutableArray<T> values) => _values = values;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Values => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    public int Count => _values.IsDefault ? 0 : _values.Length;

    public T this[int index] => _values[index];

    public bool Equals(EquatableArray<T> other)
    {
        var count = Count;
        if (count != other.Count) return false;

        var left = _values.AsSpan();
        var right = other._values.AsSpan();
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        // A default and an empty array are equal, so they must hash alike.
        if (Count == 0) return 0;

        var comparer = EqualityComparer<T>.Default;
        var hash = 17;
        foreach (var value in _values)
            hash = unchecked((hash * 31) + comparer.GetHashCode(value!));
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    // A struct enumerator, which foreach binds to without boxing; the interface overloads remain
    // for LINQ.
    public Enumerator GetEnumerator() => new(Values);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)Values).GetEnumerator();

    public struct Enumerator
    {
        private readonly ImmutableArray<T> _values;
        private int _index;

        internal Enumerator(ImmutableArray<T> values)
        {
            _values = values;
            _index = -1;
        }

        public readonly T Current => _values[_index];

        public bool MoveNext() => ++_index < _values.Length;
    }
}

/// <summary>Creates <see cref="EquatableArray{T}"/> instances.</summary>
internal static class EquatableArray
{
    public static EquatableArray<T> From<T>(IEnumerable<T> values)
        where T : IEquatable<T>
        => new(ImmutableArray.CreateRange(values));
}
