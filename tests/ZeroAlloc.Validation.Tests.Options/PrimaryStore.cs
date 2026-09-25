using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// Options models nested in other types with the same simple name, issue #207.
public static class PrimaryStore
{
    [Validate]
    public class Settings
    {
        [NotEmpty] public string Path { get; set; } = "";
    }
}
