using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Presentation.Contracts.Backtests;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     The start of the bench API (task 4.1): migrations, <c>/health</c> and the frontend in <c>wwwroot</c>.
/// </summary>
[NonParallelizable]
public sealed class StartTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private string _webRoot = null!;

    [SetUp]
    public async Task Prepare()
    {
        await BenchDatabase.Reset();
        _market = await MarketDataServer.Start();
        _webRoot = Path.Combine(Path.GetTempPath(), $"bench-wwwroot-{Guid.NewGuid():N}");
    }

    [TearDown]
    public async Task Clean()
    {
        await _market.DisposeAsync();
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    [Test]
    public async Task A_start_on_an_empty_database_migrates_it_and_serves_no_backtests()
    {
        string empty = await BenchDatabase.NewDatabase();
        await using var api = new BenchTestApplication(_market, new BenchStart(empty));

        BacktestResponse[] backtests = (await api.Http.GetFromJsonAsync<BacktestResponse[]>("/api/backtests", Json))!;

        Assert.That(backtests, Is.Empty);
        Assert.That((await api.Http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Health_is_not_ok_while_the_database_does_not_answer()
    {
        // The API migrates its database at start, so the database goes away after the start.
        string database = await BenchDatabase.NewDatabase();
        await using var api = new BenchTestApplication(_market, new BenchStart(database));
        Assert.That((await api.Http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "the API has started");

        await BenchDatabase.DropDatabase(database);

        Assert.That((await api.Http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task The_frontend_is_served_with_its_pages_falling_back_to_index_but_not_the_api_or_health()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), "<html>bench</html>");
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "app.js"), "console.log(1)");
        await using var api = new BenchTestApplication(_market, new BenchStart(WebRoot: _webRoot));

        Assert.That(await api.Http.GetStringAsync("/"), Is.EqualTo("<html>bench</html>"));
        Assert.That(await api.Http.GetStringAsync("/runs/abc"), Is.EqualTo("<html>bench</html>"), "a page of the frontend");
        Assert.That(await api.Http.GetStringAsync("/assets/app.js"), Is.EqualTo("console.log(1)"));
        Assert.That((await api.Http.GetAsync("/api/backtests")).Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        Assert.That((await api.Http.GetAsync("/api/nosuch")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "the fallback leaves /api alone");
        Assert.That((await api.Http.GetAsync("/health")).Content.Headers.ContentType!.MediaType, Is.EqualTo("text/plain"));
    }

    [Test]
    public async Task Without_a_wwwroot_folder_nothing_but_the_api_is_served()
    {
        await using var api = new BenchTestApplication(_market);

        Assert.That((await api.Http.GetAsync("/")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await api.Http.GetAsync("/runs/abc")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await api.Http.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
