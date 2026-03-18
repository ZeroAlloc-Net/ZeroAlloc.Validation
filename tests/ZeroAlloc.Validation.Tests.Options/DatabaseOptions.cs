using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

[Validate]
public class DatabaseOptions
{
    [NotEmpty]       public string ConnectionString { get; set; } = "";
    [GreaterThan(0)] public int    MaxPoolSize      { get; set; }
}
