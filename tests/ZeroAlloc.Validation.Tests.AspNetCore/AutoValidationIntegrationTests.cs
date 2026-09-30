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
    public async Task AsyncRule_Passing_Returns200()
    {
        var response = await _client!.PostAsJsonAsync("/sample/signup", new { UserName = "free" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AsyncRule_Failing_Returns422_WithItsMessage()
    {
        // The model's synchronous Validate throws, so a 422 here shows the filter awaited ValidateAsync.
        var response = await _client!.PostAsJsonAsync("/sample/signup", new { UserName = "taken-name" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("UserName 'taken-name' is taken.", body, StringComparison.Ordinal);
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
    public async Task ValidRecordModel_Returns200()
    {
        var response = await _client!.PostAsJsonAsync("/sample/record", new { Name = "Widget" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidRecordModel_EmptyName_Returns422()
    {
        var response = await _client!.PostAsJsonAsync("/sample/record", new { Name = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", body, StringComparison.Ordinal);
    }

    // Issue #207: action arguments whose [Validate] model is nested in another type.
    [Theory]
    [InlineData("/sample/nested/orders", "{\"Name\":\"Widget\"}", HttpStatusCode.OK)]
    [InlineData("/sample/nested/orders", "{\"Name\":\"\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/sample/nested/returns", "{\"Quantity\":1}", HttpStatusCode.OK)]
    [InlineData("/sample/nested/returns", "{\"Quantity\":0}", HttpStatusCode.UnprocessableEntity)]
    public async Task NestedModel_IsValidatedByFilter(string path, string json, HttpStatusCode expected)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync(path, content);
        Assert.Equal(expected, response.StatusCode);
    }

    // Issue #246: an argument whose validator composes nested and collection validators.
    [Theory]
    [InlineData("{\"Parcel\":{\"Weight\":1},\"Extras\":[{\"Weight\":2}]}", HttpStatusCode.OK, null)]
    [InlineData("{\"Parcel\":{\"Weight\":0},\"Extras\":[]}", HttpStatusCode.UnprocessableEntity, "Parcel.Weight")]
    [InlineData("{\"Parcel\":{\"Weight\":1},\"Extras\":[{\"Weight\":0}]}", HttpStatusCode.UnprocessableEntity, "Extras[0].Weight")]
    public async Task ComposedModel_IsValidatedByFilter(string json, HttpStatusCode expected, string? failedPath)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/sample/composed", content);
        Assert.Equal(expected, response.StatusCode);
        if (failedPath is not null)
            Assert.Contains(failedPath, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Issue #238: an argument whose validator composes a closing of a generic model, Crate<Parcel>,
    // registered closed by AddZeroAllocAspNetCoreValidation().
    [Theory]
    [InlineData("{\"Crate\":{\"Label\":\"c\",\"Contents\":[{\"Weight\":1}]}}", HttpStatusCode.OK, null)]
    [InlineData("{\"Crate\":{\"Label\":\"\",\"Contents\":[]}}", HttpStatusCode.UnprocessableEntity, "Crate.Label")]
    public async Task ModelComposingAGenericClosing_IsValidatedByFilter(string json, HttpStatusCode expected, string? failedPath)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/sample/delivery", content);
        Assert.Equal(expected, response.StatusCode);
        if (failedPath is not null)
            Assert.Contains(failedPath, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddZeroAllocAspNetCoreValidation_ListsTheGenericClosingAsAModelValidator()
    {
        var registry = _app!.Services.GetServices<IModelValidator>().ToList();
        Assert.Contains(registry, v => v.ModelType == typeof(Crate<Parcel>));
    }

    // Issue #238, phase 3: a closing of a generic model as the action argument itself, which the
    // filter's type-switch cannot name, is dispatched through the IModelValidator registry.
    [Theory]
    [InlineData("/sample/crate/parcel", "{\"Label\":\"c\"}", HttpStatusCode.OK)]
    [InlineData("/sample/crate/parcel", "{\"Label\":\"\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/sample/crate/sample", "{\"Label\":\"c\"}", HttpStatusCode.OK)]
    [InlineData("/sample/crate/sample", "{\"Label\":\"\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/sample/crate/special", "{\"Label\":\"c\"}", HttpStatusCode.OK)]
    [InlineData("/sample/crate/special", "{\"Label\":\"\"}", HttpStatusCode.UnprocessableEntity)]
    public async Task GenericClosingArgument_IsValidatedThroughTheRegistry(string path, string json, HttpStatusCode expected)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync(path, content);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.UnprocessableEntity)
            Assert.Contains("Label", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnregisteredClosingOfALocalGenericModel_Throws()
    {
        // Letting it through would skip validation silently; the dispatch fails loudly instead.
        using var content = new StringContent("{\"Label\":\"\"}", System.Text.Encoding.UTF8, "application/json");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _client!.PostAsync("/sample/crate/signup", content));
        Assert.Contains("Crate", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Add…Validator<…>()", exception.Message, StringComparison.Ordinal);
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
