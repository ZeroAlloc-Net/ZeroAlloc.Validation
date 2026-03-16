using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ZValidation.Tests.AspNetCore;

public static class TestApp
{
    public static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(SampleController).Assembly);

        builder.Services.AddZValidationAutoValidation();

        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
