namespace ZeroAlloc.Validation.Testing;

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

    public static void HasErrorWithMessage(ValidationResult result, string propertyName, string expectedMessage)
    {
        foreach (ref readonly ValidationFailure failure in result.Failures)
        {
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
            if (string.Equals(failure.PropertyName, propertyName, System.StringComparison.Ordinal)
                && string.Equals(failure.ErrorMessage, expectedMessage, System.StringComparison.Ordinal))
#pragma warning restore EPS06
                return;
        }
        throw new ValidationAssertException(
            $"Expected a failure for '{propertyName}' with message '{expectedMessage}' but none found.\nActual failures: {FailureSummary(result)}");
    }

    private static string FailureSummary(ValidationResult result)
    {
        var sb = new System.Text.StringBuilder();
        foreach (ref readonly var f in result.Failures)
        {
#pragma warning disable EPS06 // False positive: ValidationFailure is a readonly struct
            sb.Append($"\n  [{f.PropertyName}] {f.ErrorMessage}");
#pragma warning restore EPS06
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    public static void NoErrors(ValidationResult result)
    {
        if (!result.IsValid)
            throw new ValidationAssertException(
                $"Expected no validation errors but found {result.Failures.Length}.");
    }
}
