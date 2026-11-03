using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "История с догрузкой" and "Период истории графика" of the bot chart, with no Worker running.</summary>
public class BotCandlesApiTests
{
    private static readonly Instrument Btc = new(MarketCategory.Spot, "BTCUSDT");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _bot;

    [SetUp]
    public async Task SetUp()
    {
        // The clock reads 2024-03-10T12:30Z, so the last closed hour is 11:00.
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        _factory.Bybit.Instruments.Add(("spot", "BTCUSDT"));
        _client = _factory.CreateClient();
        _bot = await CreateBotAsync();
        foreach (int hour in Enumerable.Range(0, 24))
        {
            _factory.Bybit.Klines.Add([$"2024-03-10T{hour:00}:00:00Z", "100", "101", "99", "100.5", "7.5", "750"]);
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Пустое хранилище" and "Бэктест после графика".
    [Test]
    public async Task Empty_storage_gives_the_candles_of_bybit_and_stores_them()
    {
        JsonElement response = await GetAsync("from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z");

        response.GetProperty("interval").GetString().ShouldBe("60");
        response.GetProperty("unavailable").GetBoolean().ShouldBeFalse();
        response.GetProperty("candles").EnumerateArray().Select(candle => candle.GetProperty("openTime").GetDateTimeOffset().Hour).ShouldBe([8, 9, 10, 11]);
        response.GetProperty("candles")[0].GetProperty("volume").GetString().ShouldBe("7.5");
        (await StoredAsync()).Select(candle => candle.OpenTime.Hour).ShouldBe([8, 9, 10, 11]);
    }

    // Scenario "Повторное открытие".
    [Test]
    public async Task The_same_period_again_does_not_call_the_exchange()
    {
        await GetAsync("from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z");
        int requests = _factory.Bybit.KlineRequests.Count;

        JsonElement again = await GetAsync("from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z");

        again.GetProperty("candles").GetArrayLength().ShouldBe(4);
        _factory.Bybit.KlineRequests.Count.ShouldBe(requests);
    }

    // Scenario "Период до текущего момента".
    [Test]
    public async Task The_current_candle_is_not_in_the_answer_nor_stored()
    {
        JsonElement response = await GetAsync("from=2024-03-10T10:00:00Z&to=2024-03-10T13:00:00Z");

        response.GetProperty("candles").EnumerateArray().Select(candle => candle.GetProperty("openTime").GetDateTimeOffset().Hour).ShouldBe([10, 11]);
        (await StoredAsync()).Select(candle => candle.OpenTime.Hour).ShouldBe([10, 11]);
    }

    // Scenarios "Bybit не отвечает" and "Загрузка идёт" (no import job is created).
    [Test]
    public async Task Unavailable_bybit_gives_the_stored_candles_flagged_and_no_import_job()
    {
        await StoreAsync([new Candle(DateTimeOffset.Parse("2024-03-10T09:00:00Z"), new Price(1), new Price(1), new Price(1), new Price(1), new Volume(1), new Volume(1))]);
        _factory.Bybit.Unavailable = true;

        HttpResponseMessage http = await _client.GetAsync($"/api/bots/{_bot}/candles?from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z");
        JsonElement response = await http.Content.ReadFromJsonAsync<JsonElement>();

        http.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.GetProperty("unavailable").GetBoolean().ShouldBeTrue();
        response.GetProperty("candles").EnumerateArray().Select(candle => candle.GetProperty("openTime").GetDateTimeOffset().Hour).ShouldBe([9]);
        await using var dataSource = NpgsqlDataSource.Create(_factory.ConnectionString);
        await using NpgsqlCommand command = dataSource.CreateCommand("SELECT count(*) FROM candle_import_jobs");
        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(0);
    }

    // Scenario "Период длиннее 1000 свечей" and "Неизвестный интервал".
    [TestCase("interval=1&from=2024-03-08T12:00:00Z&to=2024-03-10T12:00:00Z", "from")]
    [TestCase("interval=2&from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z", "interval")]
    public async Task Invalid_request_is_rejected_by_parameter_without_calling_bybit(string query, string parameter)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/bots/{_bot}/candles?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe([parameter]);
        _factory.Bybit.KlineRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task Unknown_bot_is_not_found()
    {
        (await _client.GetAsync($"/api/bots/{Guid.NewGuid()}/candles?from=2024-03-10T08:00:00Z&to=2024-03-10T12:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Тот же период одновременно": the key of a candle is its open time, so racing writers leave one row each.
    [Test]
    public async Task A_running_import_and_chart_requests_of_the_same_period_leave_one_candle_per_hour()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 1 };
        factory.Bybit.Instruments.Add(("spot", "BTCUSDT"));
        factory.Bybit.Klines.AddRange(_factory.Bybit.Klines);
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage strategy = await client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        Guid ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid bot = (await (await client.PostAsJsonAsync("/api/bots", new { name = "BTC", strategyId = ema, category = "spot", symbol = "BTCUSDT", interval = "60", balance = 1000 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        JsonElement job = await (await client.PostAsJsonAsync(
            "/api/market-data/candle-imports",
            new { category = "spot", symbol = "BTCUSDT", interval = "60", from = "2024-03-10T00:00:00Z", to = "2024-03-10T12:00:00Z" })).Content.ReadFromJsonAsync<JsonElement>();

        Task<HttpResponseMessage>[] charts = [.. Enumerable.Range(0, 8).Select(_ => client.GetAsync($"/api/bots/{bot}/candles?from=2024-03-10T00:00:00Z&to=2024-03-10T12:00:00Z"))];
        var waiting = System.Diagnostics.Stopwatch.StartNew();

        while (job.GetProperty("status").GetString() is not ("Succeeded" or "Failed") && waiting.Elapsed < TimeSpan.FromSeconds(30))
        {
            factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
            job = await client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{job.GetProperty("id").GetGuid()}");
        }

        (await Task.WhenAll(charts)).ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        await using var dataSource = NpgsqlDataSource.Create(factory.ConnectionString);
        await using NpgsqlCommand command = dataSource.CreateCommand("SELECT count(*), count(DISTINCT open_time) FROM candles");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        (reader.GetInt64(0), reader.GetInt64(1)).ShouldBe((12L, 12L));
    }

    private async Task<JsonElement> GetAsync(string query)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/bots/{_bot}/candles?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateBotAsync()
    {
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        Guid ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/bots",
            new { name = "BTC EMA час", strategyId = ema, category = "spot", symbol = "BTCUSDT", interval = "60", balance = 1000 });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<IReadOnlyList<Candle>> StoredAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_factory.ConnectionString);

        return await new CandleRepository(dataSource).GetAsync(Btc, Interval.OneHour, DateTimeOffset.Parse("2024-03-01T00:00:00Z"), DateTimeOffset.Parse("2024-03-20T00:00:00Z"), 5000, CancellationToken.None);
    }

    private async Task StoreAsync(IReadOnlyCollection<Candle> candles)
    {
        await using var dataSource = NpgsqlDataSource.Create(_factory.ConnectionString);
        await new CandleRepository(dataSource).UpsertAsync(Btc, Interval.OneHour, candles, CancellationToken.None);
    }
}