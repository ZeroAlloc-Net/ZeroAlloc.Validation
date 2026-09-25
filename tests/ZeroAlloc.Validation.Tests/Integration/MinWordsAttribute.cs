using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

public sealed class MinWordsAttribute(int minWords) : ValidationAttribute<string?>
{
    public override bool IsValid(string? value) => CountWords(value) >= minWords;

    private static int CountWords(string? value)
    {
        if (value is null) return 0;

        var count = 0;
        var inWord = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }
}
