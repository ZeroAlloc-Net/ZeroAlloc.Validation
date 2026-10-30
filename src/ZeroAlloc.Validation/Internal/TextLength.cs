namespace ZeroAlloc.Validation.Internal;

/// <summary>
/// Counts the length of a string for the length rules with <see cref="TextLengthUnit.CodePoints"/>,
/// without allocating.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class TextLength
{
    /// <summary>
    /// The number of Unicode code points in <paramref name="text"/>: its UTF-16 length less one
    /// for each high surrogate followed by a low surrogate. A lone surrogate, which is not part of
    /// a pair, counts as one, as <see cref="System.Text.Rune"/> decoding replaces it with one
    /// U+FFFD.
    /// </summary>
    public static int CountCodePoints(System.ReadOnlySpan<char> text)
    {
        var count = text.Length;
        // Text inside the Basic Multilingual Plane has no high surrogate, so the vectorized search
        // returns at once and the length is the count. SearchValues, not IndexOfAnyInRange: the
        // allocation tests measured 96 bytes a call for IndexOfAnyInRange on .NET 10, and none
        // for SearchValues.
        var at = text.IndexOfAny(HighSurrogates);
        while (at >= 0 && at < text.Length - 1)
        {
            if (char.IsLowSurrogate(text[at + 1]))
            {
                count--;
                at++;
            }

            var next = text[(at + 1)..].IndexOfAny(HighSurrogates);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return count;
    }

    private static readonly System.Buffers.SearchValues<char> HighSurrogates = CreateHighSurrogates();

    private static System.Buffers.SearchValues<char> CreateHighSurrogates()
    {
        System.Span<char> all = stackalloc char[0xDC00 - 0xD800];
        for (var i = 0; i < all.Length; i++)
            all[i] = (char)(0xD800 + i);
        return System.Buffers.SearchValues.Create(all);
    }
}
