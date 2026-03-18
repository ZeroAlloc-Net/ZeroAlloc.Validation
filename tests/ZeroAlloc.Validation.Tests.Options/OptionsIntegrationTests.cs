using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZeroAlloc.Validation.Tests.Options;

public class OptionsIntegrationTests
{
    [Fact]
    public void ValidateWithZeroAlloc_ValidOptions_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = "Server=localhost"; o.MaxPoolSize = 10; })
            .ValidateWithZeroAlloc();

        var sp     = services.BuildServiceProvider();
        var result = sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;

        Assert.Equal("Server=localhost", result.ConnectionString);
    }

    [Fact]
    public void ValidateWithZeroAlloc_InvalidOptions_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = ""; o.MaxPoolSize = 0; }) // both invalid
            .ValidateWithZeroAlloc();

        var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Fact]
    public void ValidateWithZeroAlloc_RegistersValidatorForT_InDI()
    {
        var services = new ServiceCollection();
        services.AddOptions<SmtpOptions>()
            .Configure(o => { o.Host = "smtp.example.com"; o.Port = 587; })
            .ValidateWithZeroAlloc();

        var sp        = services.BuildServiceProvider();
        var validator = sp.GetService<ValidatorFor<SmtpOptions>>();

        Assert.NotNull(validator);
    }

    [Fact]
    public void ValidateWithZeroAlloc_CalledTwice_NoDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = "Server=localhost"; o.MaxPoolSize = 10; })
            .ValidateWithZeroAlloc()
            .ValidateWithZeroAlloc(); // second call — TryAdd should make this a no-op

        var sp = services.BuildServiceProvider();

        var validators = sp.GetServices<IValidateOptions<DatabaseOptions>>();
#pragma warning disable HLQ005 // xUnit Assert.Single is not LINQ Single
        Assert.Single(validators);
#pragma warning restore HLQ005
    }

    [Fact]
    public void TwoOptionsClasses_BothValidated_Independently()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(o => { o.ConnectionString = ""; o.MaxPoolSize = 0; }) // invalid
            .ValidateWithZeroAlloc();
        services.AddOptions<SmtpOptions>()
            .Configure(o => { o.Host = "smtp.example.com"; o.Port = 587; }) // valid
            .ValidateWithZeroAlloc();

        var sp = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        // SmtpOptions is valid — no exception
        var smtp = sp.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal("smtp.example.com", smtp.Host);
    }
}
