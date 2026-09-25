using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZeroAlloc.Validation.Options;

namespace ZeroAlloc.Validation.Tests.Options;

// Issue #207: ValidateWithZeroAlloc for options models nested in other types.
public class NestedOptionsIntegrationTests
{
    [Fact]
    public void NestedOptions_Valid_PassValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<PrimaryStore.Settings>()
            .Configure(o => o.Path = "/data")
            .ValidateWithZeroAlloc();
        services.AddOptions<ReplicaStore.Region.Settings>()
            .Configure(o => o.Copies = 2)
            .ValidateWithZeroAlloc();

        var sp = services.BuildServiceProvider();

        Assert.Equal("/data", sp.GetRequiredService<IOptions<PrimaryStore.Settings>>().Value.Path);
        Assert.Equal(2, sp.GetRequiredService<IOptions<ReplicaStore.Region.Settings>>().Value.Copies);
    }

    [Fact]
    public void NestedOptions_Invalid_FailValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<PrimaryStore.Settings>()
            .Configure(o => o.Path = "")
            .ValidateWithZeroAlloc();
        services.AddOptions<ReplicaStore.Region.Settings>()
            .Configure(o => o.Copies = 0)
            .ValidateWithZeroAlloc();

        var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<PrimaryStore.Settings>>().Value);
        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<ReplicaStore.Region.Settings>>().Value);
    }
}
