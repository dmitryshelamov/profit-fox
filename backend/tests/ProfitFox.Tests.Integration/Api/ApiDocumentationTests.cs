using System.Net;
using System.Text.Json;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class ApiDocumentationTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Environment = "Production" };
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Документ в Production".
    [Test]
    public async Task OpenApi_document_is_served_in_production()
    {
        HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("paths").TryGetProperty("/api/market-data/candles", out _).ShouldBeTrue();
    }

    [Test]
    public async Task OpenApi_document_lists_the_strategy_endpoints()
    {
        using var document = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        string[] expected = ["/api/strategies", "/api/strategies/{id}", "/api/strategy-nodes", "/api/strategy-nodes/{id}", "/api/strategy-nodes/{id}/clone"];
        string[] documented = [.. paths.EnumerateObject().Select(path => path.Name)];
        expected.Except(documented).ShouldBeEmpty();
    }

    // Scenario "Страница в Production".
    [Test]
    public async Task Swagger_ui_is_served_in_production()
    {
        HttpResponseMessage response = await _client.GetAsync("/swagger/index.html");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    // Scenario "Страница указывает на документ API". Swagger UI reads its configuration from index.js.
    [Test]
    public async Task Swagger_ui_loads_the_openapi_document()
    {
        HttpResponseMessage response = await _client.GetAsync("/swagger/index.js");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("/openapi/v1.json");
    }
}