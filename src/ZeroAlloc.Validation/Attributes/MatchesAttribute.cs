namespace ZeroAlloc.Validation;

public sealed class MatchesAttribute(string pattern) : ValidationAttribute
{
    public string Pattern { get; } = pattern;
}
