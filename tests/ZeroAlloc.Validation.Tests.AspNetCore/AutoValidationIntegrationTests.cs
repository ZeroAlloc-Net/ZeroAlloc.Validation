using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable MA0004 // ConfigureAwait: suppressed because xUnit1030 prohibits ConfigureAwait in test methods

namespace ZeroAlloc.Validation.Tests.AspNetCore;

public class AutoValidationIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _app = TestApp.Build();
        await _app.StartAsync().ConfigureAwait(false);
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ValidModel_Returns200()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "Widget", Quantity = 5 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidModel_EmptyName_Returns422()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "", Quantity = 5 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidModel_NegativeQuantity_Returns422()
    {
        var response = await _client!.PostAsJsonAsync("/sample", new { Name = "Widget", Quantity = 0 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Quantity", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownModelType_FilterSkips_Returns200()
    {
        using var content = new StringContent("\"hello\"", System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/sample/unknown", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void AddZeroAllocAspNetCoreValidation_RegistersFilter()
    {
        var filter = _app!.Services.GetService<ZeroAllocValidationActionFilter>();
        Assert.NotNull(filter);
    }
}
