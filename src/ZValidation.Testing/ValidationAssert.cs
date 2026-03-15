namespace ZValidation.Testing;

public static class ValidationAssert
{
    public static void HasError(ValidationResult result, string propertyName)
    {
        foreach (var failure in result.Failures)
        {
            if (failure.PropertyName == propertyName)
                return;
        }
        throw new ValidationAssertException(
            $"Expected a validation error for '{propertyName}' but none was found.");
    }

    public static void NoErrors(ValidationResult result)
    {
        if (!result.IsValid)
            throw new ValidationAssertException(
                $"Expected no validation errors but found {result.Failures.Length}.");
    }
}

public sealed class ValidationAssertException(string message) : Exception(message);
