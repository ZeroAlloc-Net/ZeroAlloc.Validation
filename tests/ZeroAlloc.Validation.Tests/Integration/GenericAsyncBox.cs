using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Integration;

/// <summary>A generic model with an asynchronous rule, issue #238 with #202.</summary>
[Validate]
public class GenericAsyncBox<T>
{
    [UniqueName]
    public string? Name { get; set; }

    [NotNull]
    public T? Value { get; set; }
}
