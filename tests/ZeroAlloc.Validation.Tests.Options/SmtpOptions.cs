using ZeroAlloc.Validation;

namespace ZeroAlloc.Validation.Tests.Options;

[Validate]
public class SmtpOptions
{
    [NotEmpty]       public string Host { get; set; } = "";
    [GreaterThan(0)] public int    Port { get; set; }
}
