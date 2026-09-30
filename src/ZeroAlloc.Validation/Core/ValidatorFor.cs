namespace ZeroAlloc.Validation;

public abstract partial class ValidatorFor<T> : IModelValidator
{
    public abstract ValidationResult Validate(T instance);

    // Default: wraps sync Validate in a completed ValueTask.
    // Overridden by the generator when async pipeline behaviors are present, and for a model
    // with asynchronous rules, AsyncValidationAttribute<T>, whose sync Validate then throws.
    public virtual global::System.Threading.Tasks.ValueTask<ValidationResult> ValidateAsync(
        T instance,
        global::System.Threading.CancellationToken ct = default)
        => global::System.Threading.Tasks.ValueTask.FromResult(Validate(instance));

    Type IModelValidator.ModelType => typeof(T);

    // Implemented explicitly, so no generated or hand-written validator changes. A value-type
    // model arrives boxed and is unboxed here, without allocating.
    global::System.Threading.Tasks.ValueTask<ValidationResult> IModelValidator.ValidateAsync(
        object instance,
        global::System.Threading.CancellationToken ct)
        => ValidateAsync((T)instance, ct);
}
