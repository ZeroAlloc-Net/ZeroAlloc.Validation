namespace ZValidation.Testing;

public static class ValidationAssert
{
    public static void HasError(ValidationResult result, string propertyName)
    {
        foreach (ref readonly ValidationFailure failure in result.Failures)
        {
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
            if (string.Equals(failure.PropertyName, propertyName, System.StringComparison.Ordinal))
#pragma warning restore EPS06
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
