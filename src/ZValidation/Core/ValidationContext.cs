namespace ZValidation;

public readonly ref struct ValidationContext<T>
{
    public T Instance { get; }

    public ValidationContext(T instance)
    {
        Instance = instance;
    }
}
