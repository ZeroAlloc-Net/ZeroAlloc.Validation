using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZeroAlloc.Validation.Options;

namespace ZeroAlloc.Validation.Tests.Options;

// Issue #238: ValidateWithZeroAlloc() registers the closing of a generic section closed, so the
// options model's validator is built from it alone. A closing used as the options model itself
// gets the generic overload, which inference closes from the builder.
public class GenericSectionOptionsTests
{
    [Fact]
    public void GenericOptionsModel_Valid_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<EndpointOptions<TlsOptions>>()
            .Configure(o => o.Url = "https://upstream")
            .ValidateWithZeroAlloc();

        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Equal("https://upstream", sp.GetRequiredService<IOptions<EndpointOptions<TlsOptions>>>().Value.Url);
        Assert.IsType<EndpointOptionsValidator<TlsOptions>>(sp.GetRequiredService<ZeroAlloc.Validation.ValidatorFor<EndpointOptions<TlsOptions>>>());
    }

    [Fact]
    public void GenericOptionsModel_Invalid_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<EndpointOptions<TlsOptions>>()
            .Configure(o =>
            {
                o.Url = "";
                o.Security = null;
            })
            .ValidateWithZeroAlloc();

        using var sp = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<EndpointOptions<TlsOptions>>>().Value);
        Assert.Contains(exception.Failures, f => f.Contains("Url", System.StringComparison.Ordinal));
        Assert.Contains(exception.Failures, f => f.Contains("Security", System.StringComparison.Ordinal));
    }

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
