using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class CandlesApiTests
{
    private const string Candles = "/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60";
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Свечи есть".
    [Test]
    public async Task Returns_candles_of_the_period_oldest_first()
    {
        await StoreHourlyCandlesAsync(3);

        List<JsonElement> candles = await GetCandlesAsync($"{Candles}&from=2024-01-01T01:00:00Z&to=2024-01-01T03:00:00Z");

        candles.Select(c => c.GetProperty("openTime").GetDateTimeOffset()).ShouldBe(new[] { Day.AddHours(1), Day.AddHours(2) });
    }

    [Test]
    public async Task Period_with_any_offset_is_read_as_utc()
    {
        await StoreHourlyCandlesAsync(3);

        List<JsonElement> candles = await GetCandlesAsync($"{Candles}&from=2024-01-01T04:00:00%2B03:00&to=2024-01-01T06:00:00%2B03:00");

        candles.Select(c => c.GetProperty("openTime").GetDateTimeOffset()).ShouldBe(new[] { Day.AddHours(1), Day.AddHours(2) });
    }

    // Scenario "Свечей нет".
    [Test]
    public async Task Returns_an_empty_list_when_nothing_was_loaded()
    {
        List<JsonElement> candles = await GetCandlesAsync($"{Candles}&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z");

        candles.ShouldBeEmpty();
    }

    // Scenario "Малый объём".
    [Test]
    public async Task Values_are_exact_decimal_strings()
    {
        await StoreAsync([
            new Candle(Day, new Price(42000.1m), new Price(42300.5m), new Price(41950m), new Price(42123.45m), new Volume(0.000123m), new Volume(5.18m)),
        ]);

        JsonElement candle = (await GetCandlesAsync($"{Candles}&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z")).Single();

        (new[] { "open", "high", "low", "close", "volume", "turnover" }.Select(name => candle.GetProperty(name).GetString())).ShouldBe(new[] { "42000.1", "42300.5", "41950", "42123.45", "0.000123", "5.18" });
    }

    // Scenario "Свечей больше лимита".
    [Test]
    public async Task Returns_the_first_1000_candles_by_default()
    {
        await StoreHourlyCandlesAsync(1500);

        List<JsonElement> candles = await GetCandlesAsync($"{Candles}&from=2024-01-01T00:00:00Z&to=2025-01-01T00:00:00Z");

        candles.Count.ShouldBe(1000);
        candles[^1].GetProperty("openTime").GetDateTimeOffset().ShouldBe(Day.AddHours(999));
    }

    [Test]
    public async Task Returns_at_most_limit_candles()
    {
        await StoreHourlyCandlesAsync(3);

        List<JsonElement> candles = await GetCandlesAsync($"{Candles}&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z&limit=2");

        candles.Count.ShouldBe(2);
    }

    // Scenarios "Лимит больше максимума", "Некорректный период" and "Время без смещения", plus the other parameters.
    [TestCase("category=inverse&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z", "category")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=2&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z", "interval")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-02T00:00:00Z&to=2024-01-01T00:00:00Z", "from")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&to=2024-01-02T00:00:00Z", "from")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z", "to")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00&to=2024-01-02T00:00:00Z", "from")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00", "to")]
    [TestCase("category=linear&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z", "symbol")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z&limit=10000", "limit")]
    [TestCase("category=linear&symbol=BTCUSDT&interval=60&from=2024-01-01T00:00:00Z&to=2024-01-02T00:00:00Z&limit=0", "limit")]
    public async Task Invalid_request_is_rejected_with_the_invalid_parameter(string query, string invalidField)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/market-data/candles?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(new[] { invalidField });
    }

    private async Task<List<JsonElement>> GetCandlesAsync(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    private Task StoreHourlyCandlesAsync(int count) =>
        StoreAsync(Enumerable.Range(0, count)
            .Select(hour => CandleGenerator.Generate(Day.AddHours(hour)))
            .ToList());

    private async Task StoreAsync(IReadOnlyCollection<Candle> candles)
    {
        await using var dataSource = NpgsqlDataSource.Create(_factory.ConnectionString);
        await new CandleRepository(dataSource).UpsertAsync(new Instrument(MarketCategory.Linear, "BTCUSDT"), Interval.OneHour, candles, CancellationToken.None);
    }
}
