using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZeroAlloc.Validation.Options;

namespace ZeroAlloc.Validation.Tests.Options;

// Issue #238: ValidateWithZeroAlloc() registers the closing of a generic section closed, so the
// options model's validator is built from it alone.
public class GenericSectionOptionsTests
{
    [Fact]
    public void GenericSection_Valid_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<GatewayOptions>()
            .Configure(o =>
            {
                o.Name = "gw";
                o.Upstream.Url = "https://upstream";
            })
            .ValidateWithZeroAlloc();

        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Equal("https://upstream", sp.GetRequiredService<IOptions<GatewayOptions>>().Value.Upstream.Url);
        Assert.NotNull(sp.GetService<ZeroAlloc.Validation.ValidatorFor<EndpointOptions<TlsOptions>>>());
    }

    [Fact]
    public void GenericSection_Invalid_FailsValidationWithItsPath()
    {
        var services = new ServiceCollection();
        services.AddOptions<GatewayOptions>()
            .Configure(o =>
            {
                o.Name = "gw";
                o.Upstream.Url = "";
                o.Upstream.Security = null;
            })
            .ValidateWithZeroAlloc();

        using var sp = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<GatewayOptions>>().Value);
        Assert.Contains(exception.Failures, f => f.Contains("Upstream.Url", System.StringComparison.Ordinal));
        Assert.Contains(exception.Failures, f => f.Contains("Upstream.Security", System.StringComparison.Ordinal));
    }
}
