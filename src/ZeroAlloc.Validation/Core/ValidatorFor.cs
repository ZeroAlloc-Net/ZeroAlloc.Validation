namespace ZeroAlloc.Validation;

public abstract partial class ValidatorFor<T>
{
    public abstract ValidationResult Validate(T instance);
}
