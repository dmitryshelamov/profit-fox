using System.Globalization;
using System.Net;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Чтение снимка рынка" and "Строка пары".</summary>
public class MarketApiTests
{
    // The clock of the app reads 2024-03-10T12:30Z.
    private static readonly DateTimeOffset SnapshotAt = DateTimeOffset.Parse("2024-03-10T12:29:10Z", CultureInfo.InvariantCulture);

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;
    private readonly Guid _holder = Guid.NewGuid();

    [SetUp]
    public async Task SetUp()
    {
        _factory = ProfitFoxFactory.Create(workers: 0);
        _client = _factory.CreateClient();
        _database = NpgsqlDataSource.Create(_factory.ConnectionString);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _database.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenario "Снимок есть".
    [Test]
    public async Task Snapshot_comes_with_the_state_of_the_scanner_as_decimal_strings()
    {
        await SaveSnapshotAsync(
            new MarketSnapshotRow(
                new Instrument(MarketCategory.Linear, "SOLUSDT"),
                SnapshotAt,
                new PairMetrics(150.125m, 3m, 28.75m, -17.6m, 106m, -50m, 120000.5m, 1500000m, 200500m, 25m, -20m, -0.00012345m, 0.0123456789m),
                false),
            new MarketSnapshotRow(
                new Instrument(MarketCategory.Spot, "BTCUSDT"),
                SnapshotAt,
                new PairMetrics(64000.5m, null, null, null, null, null, null, null, null, null, null, null, 0.5m),
                true));
        var state = new ScannerStateRepository(_database);
        await state.SetProgressAsync(1104, 312, CancellationToken.None);
        await state.PauseRestAsync(SnapshotAt.AddMinutes(10), CancellationToken.None);

        JsonElement market = await GetAsync("/api/market");

        market.GetProperty("snapshotAt").GetDateTimeOffset().ShouldBe(SnapshotAt);
        (market.GetProperty("history").GetProperty("loaded").GetInt32(), market.GetProperty("history").GetProperty("total").GetInt32()).ShouldBe((312, 1104));
        market.GetProperty("restPausedUntil").GetDateTimeOffset().ShouldBe(SnapshotAt.AddMinutes(10));
        JsonElement sol = market.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("symbol").GetString() == "SOLUSDT");
        sol.GetProperty("category").GetString().ShouldBe("linear");
        sol.GetProperty("computedAt").GetDateTimeOffset().ShouldBe(SnapshotAt);
        sol.GetProperty("price").GetString().ShouldBe("150.125");
        sol.GetProperty("change5m").GetString().ShouldBe("3");
        sol.GetProperty("change15m").GetString().ShouldBe("28.75");
        sol.GetProperty("change1h").GetString().ShouldBe("-17.6");
        sol.GetProperty("change4h").GetString().ShouldBe("106");
        sol.GetProperty("change24h").GetString().ShouldBe("-50");
        sol.GetProperty("turnover1h").GetString().ShouldBe("120000.5");
        sol.GetProperty("turnover24h").GetString().ShouldBe("1500000");
        sol.GetProperty("openInterest").GetString().ShouldBe("200500");
        sol.GetProperty("openInterestChange1h").GetString().ShouldBe("25");
        sol.GetProperty("openInterestChange24h").GetString().ShouldBe("-20");
        sol.GetProperty("fundingRate").GetString().ShouldBe("-0.00012345");
        sol.GetProperty("spread").GetString().ShouldBe("0.0123456789");
        sol.GetProperty("incomplete").GetBoolean().ShouldBeFalse();
        JsonElement btc = market.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("symbol").GetString() == "BTCUSDT");
        btc.GetProperty("change5m").ValueKind.ShouldBe(JsonValueKind.Null);
        btc.GetProperty("openInterest").ValueKind.ShouldBe(JsonValueKind.Null);
        btc.GetProperty("incomplete").GetBoolean().ShouldBeTrue();
        (market.GetProperty("page").GetInt32(), market.GetProperty("pageSize").GetInt32(), market.GetProperty("total").GetInt32(), market.GetProperty("pairs").GetInt32()).ShouldBe((1, 50, 2, 2));
        _factory.Bybit.Requests.ShouldBe(0);
    }

    // Scenario "Чипы считают остальные фильтры".
    [Test]
    public async Task The_chips_count_the_pairs_left_by_the_category_the_search_and_the_filter()
    {
        await SaveSnapshotAsync(
            Row(MarketCategory.Linear, "AAAUSDT"),
            Row(MarketCategory.Linear, "BBBUSDT"),
            Row(MarketCategory.Linear, "CCCUSDC"),
            Row(MarketCategory.Spot, "DDDUSDT"));
        await new MarketSnapshotRepository(_database).ReplaceSetupsAsync(
            _holder,
            new Dictionary<Instrument, IReadOnlyList<PairSetup>>
            {
                [new Instrument(MarketCategory.Linear, "AAAUSDT")] = [new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)],
                [new Instrument(MarketCategory.Linear, "CCCUSDC")] = [new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)],
                [new Instrument(MarketCategory.Spot, "DDDUSDT")] = [new PairSetup(Interval.FifteenMinutes, IdeaSetup.Impulse)]
            },
            CancellationToken.None);

        JsonElement market = await GetAsync("/api/market?category=linear&search=usdt&setup=impulse&interval=15");

        market.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("symbol").GetString()).ShouldBe(["AAAUSDT"]);
        (market.GetProperty("total").GetInt32(), market.GetProperty("pairs").GetInt32()).ShouldBe((1, 4));
        (market.GetProperty("categories").GetProperty("linear").GetInt32(), market.GetProperty("categories").GetProperty("spot").GetInt32()).ShouldBe((3, 1));
        JsonElement setups = market.GetProperty("setups");
        (setups.GetProperty("all").GetInt32(), setups.GetProperty("impulse").GetInt32(), setups.GetProperty("drop").GetInt32()).ShouldBe((2, 1, 0));
    }

    [TestCase("pageSize=75", "pageSize")]
    [TestCase("page=0", "page")]
    [TestCase("category=inverse", "category")]
    [TestCase("setup=rocket", "setup")]
    [TestCase("interval=30", "interval")]
    [TestCase("sort=volume", "sort")]
    [TestCase("direction=up", "direction")]
    [TestCase("savedFilter=00000000-0000-0000-0000-000000000001", "savedFilter")]
    public async Task A_wrong_filter_order_or_page_is_a_validation_error_by_the_parameter(string query, string key)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/market?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty(key, out _).ShouldBeTrue();
    }

    // Scenario "Вторая страница рынка".
    [Test]
    public async Task The_second_page_holds_the_next_pairs_in_the_order_asked_for()
    {
        await SaveSnapshotAsync([.. Enumerable.Range(0, 120).Select(number => Row(MarketCategory.Linear, $"P{number:000}USDT", turnover24h: number))]);

        JsonElement market = await GetAsync("/api/market?sort=turnover24h&direction=asc&page=2&pageSize=100");

        (market.GetProperty("items").GetArrayLength(), market.GetProperty("page").GetInt32(), market.GetProperty("total").GetInt32()).ShouldBe((20, 2, 120));
        market.GetProperty("items")[0].GetProperty("symbol").GetString().ShouldBe("P100USDT");
    }

    // Scenario "Сканер ни разу не работал".
    [Test]
    public async Task Market_without_a_snapshot_has_no_rows_and_no_time()
    {
        JsonElement market = await GetAsync("/api/market");

        market.GetProperty("items").GetArrayLength().ShouldBe(0);
        market.GetProperty("snapshotAt").ValueKind.ShouldBe(JsonValueKind.Null);
        market.GetProperty("restPausedUntil").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // A pause that has ended is no pause.
    [Test]
    public async Task Pause_that_has_ended_is_not_shown()
    {
        await new ScannerStateRepository(_database).PauseRestAsync(SnapshotAt, CancellationToken.None);

        (await GetAsync("/api/market")).GetProperty("restPausedUntil").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Row_of_a_pair_comes_alone()
    {
        await SaveSnapshotAsync(new MarketSnapshotRow(
            new Instrument(MarketCategory.Spot, "BTCUSDT"),
            SnapshotAt,
            new PairMetrics(64000.5m, 1m, null, null, null, null, null, null, null, null, null, null, 0.5m),
            false));

        JsonElement row = await GetAsync("/api/market/spot/BTCUSDT");

        (row.GetProperty("category").GetString(), row.GetProperty("symbol").GetString(), row.GetProperty("change5m").GetString()).ShouldBe(("spot", "BTCUSDT", "1"));
    }

    // Scenario "Пары нет в снимке".
    [Test]
    public async Task Pair_out_of_the_snapshot_is_not_found()
    {
        (await _client.GetAsync("/api/market/spot/NOPEUSDT")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Unknown_category_is_a_validation_error()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market/inverse/BTCUSD");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("category");
    }

    private static MarketSnapshotRow Row(MarketCategory category, string symbol, decimal? turnover24h = null)
    {
        return new MarketSnapshotRow(new Instrument(category, symbol), SnapshotAt, new PairMetrics(1m, null, null, null, null, null, null, turnover24h, null, null, null, null, null), false);
    }

    private async Task SaveSnapshotAsync(params MarketSnapshotRow[] rows)
    {
        await new ScannerStateRepository(_database).ClaimAsync(_holder, SnapshotAt, SnapshotAt.AddSeconds(30), CancellationToken.None);
        await new MarketSnapshotRepository(_database).ReplaceAsync(_holder, rows, SnapshotAt, CancellationToken.None);
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        HttpResponseMessage response = await _client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}