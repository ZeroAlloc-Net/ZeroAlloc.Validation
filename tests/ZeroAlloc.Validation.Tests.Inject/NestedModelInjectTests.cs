using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Inject;

// Issue #207: AddZeroAllocValidators registers validators for models nested in other types.
public class NestedModelInjectTests
{
    [Fact]
    public void AddZeroAllocValidators_ResolvesAndRunsNestedModelValidators()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocValidators();
        var sp = services.BuildServiceProvider();

        var billing  = sp.GetRequiredService<ValidatorFor<Billing.Request>>();
        var shipping = sp.GetRequiredService<ValidatorFor<Shipping.Request>>();

        Assert.True(billing.Validate(new Billing.Request { Account = "A-1" }).IsValid);
        Assert.False(billing.Validate(new Billing.Request { Account = "" }).IsValid);
        Assert.True(shipping.Validate(new Shipping.Request { Parcels = 1 }).IsValid);
        Assert.False(shipping.Validate(new Shipping.Request { Parcels = 0 }).IsValid);
    }
}
