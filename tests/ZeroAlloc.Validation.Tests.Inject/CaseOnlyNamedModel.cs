using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Inject;

// Nested properties whose names differ only in case: the validator's constructor takes
// addressValidator and address2Validator, and the container must still build it.
[Validate]
public class CaseOnlyNamedModel
{
    public ApiKeyOptions? Address { get; set; }

    public ApiKeyOptions? address { get; set; }
}
