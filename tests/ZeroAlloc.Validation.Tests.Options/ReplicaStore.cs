using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

// Options models nested in other types with the same simple name, issue #207.
public static class ReplicaStore
{
    public static class Region
    {
        [Validate]
        public class Settings
        {
            [GreaterThan(0)] public int Copies { get; set; }
        }
    }
}
