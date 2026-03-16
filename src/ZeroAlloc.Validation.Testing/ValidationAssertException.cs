namespace ZValidation.Testing;

public sealed class ValidationAssertException : System.Exception
{
    public ValidationAssertException() { }
    public ValidationAssertException(string message) : base(message) { }
    public ValidationAssertException(string message, System.Exception innerException) : base(message, innerException) { }
}
