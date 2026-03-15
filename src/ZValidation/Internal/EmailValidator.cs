namespace ZValidationInternal;

internal static class EmailValidator
{
    // Zero-alloc check: must have exactly one '@' with chars before it and a '.' in the domain part.
    internal static bool IsValid(string? email)
    {
        if (string.IsNullOrEmpty(email)) return false;
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return false;
        // Find last '.' in the domain portion using the string directly (no span copy)
        var lastDot = email.LastIndexOf('.');
        return lastDot > atIndex + 1 && lastDot < email.Length - 1;
    }
}
