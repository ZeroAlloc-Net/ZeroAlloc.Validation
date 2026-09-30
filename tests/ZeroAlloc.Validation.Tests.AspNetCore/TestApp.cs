using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

public static class TestApp
{
    public static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(SampleController).Assembly);

        builder.Services.AddZeroAllocAspNetCoreValidation();

        // A closing used only as an action argument, registered through the helper, #238.
        builder.Services.AddCrateValidator<SampleModel>();

        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
