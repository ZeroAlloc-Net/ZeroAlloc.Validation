using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZeroAlloc.Validation.Options;

namespace ZeroAlloc.Validation.Tests.Options;

// Issue #246: ValidateWithZeroAlloc alone registers the validators a composed validator takes.
public class ComposedOptionsIntegrationTests
{
    private static ServiceProvider Build(ClusterOptions configured)
    {
        var services = new ServiceCollection();
        services.AddOptions<ClusterOptions>()
            .Configure(o =>
            {
                o.Name = configured.Name;
                o.Leader = configured.Leader;
                o.Followers = configured.Followers;
            })
            .ValidateWithZeroAlloc();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    [Fact]
    public void ComposedOptions_Valid_PassValidation()
    {
        using var sp = Build(new ClusterOptions
        {
            Name = "c1",
            Leader = new NodeOptions { Port = 1 },
            Followers = [new NodeOptions { Port = 2 }],
        });

        Assert.Equal("c1", sp.GetRequiredService<IOptions<ClusterOptions>>().Value.Name);
    }

    [Fact]
    public void ComposedOptions_InvalidNestedAndElement_FailValidation()
    {
        using var sp = Build(new ClusterOptions
        {
            Name = "c1",
            Leader = new NodeOptions { Port = 0 },
            Followers = [new NodeOptions { Port = 2 }, new NodeOptions { Port = 0 }],
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<ClusterOptions>>().Value);
        Assert.Contains(ex.Failures, f => f.Contains("Leader.Port", System.StringComparison.Ordinal));
        Assert.Contains(ex.Failures, f => f.Contains("Followers[1].Port", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ComposedOptions_InvalidThirdLevel_FailValidation()
    {
        using var sp = Build(new ClusterOptions
        {
            Name = "c1",
            Leader = new NodeOptions { Port = 1, Tls = new TlsOptions { Version = 0 } },
            Followers = [new NodeOptions { Port = 2, Tls = new TlsOptions { Version = 0 } }],
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<ClusterOptions>>().Value);
        Assert.Contains(ex.Failures, f => f.Contains("Leader.Tls.Version", System.StringComparison.Ordinal));
        Assert.Contains(ex.Failures, f => f.Contains("Followers[0].Tls.Version", System.StringComparison.Ordinal));
    }
}
