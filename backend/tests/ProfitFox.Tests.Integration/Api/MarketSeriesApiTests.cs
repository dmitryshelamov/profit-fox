using System.Globalization;
using System.Net;
using System.Text.Json;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Открытый интерес и фандинг пары": from storage only.</summary>
public class MarketSeriesApiTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = await ProfitFoxFactory.CreateAsync(workers: 0);
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

    // Scenario "Фандинг за 10 дней".
    [Test]
    public async Task Funding_of_the_period_comes_with_the_time_of_each_settlement()
    {
        await new FundingRateRepository(_database).UpsertAsync(
            Sol,
            [new FundingRatePoint(Start.AddHours(-8), 1m), new FundingRatePoint(Start, 0.0001m), new FundingRatePoint(Start.AddHours(8), -0.00012345m), new FundingRatePoint(Start.AddDays(10), 2m)],
            CancellationToken.None);

        JsonElement rates = await GetAsync("/api/market/linear/SOLUSDT/funding?from=2024-03-01T00:00:00Z&to=2024-03-11T00:00:00Z");

        rates.EnumerateArray().Select(rate => (rate.GetProperty("time").GetDateTimeOffset(), rate.GetProperty("rate").GetString())).ShouldBe(
            [(Start, "0.0001"), (Start.AddHours(8), "-0.00012345")]);
        _factory.Bybit.Requests.ShouldBe(0);
    }

    [Test]
    public async Task Open_interest_of_the_period_comes_in_usdt_at_the_price_of_its_time()
    {
        await new OpenInterestRepository(_database).UpsertAsync(Sol, [new OpenInterestPoint(Start, 1234567.8m), new OpenInterestPoint(Start.AddMinutes(5), 1234570m)], CancellationToken.None);
        await new CandleRepository(_database).UpsertAsync(
            Sol,
            Interval.OneMinute,
            [new Candle(Start.AddMinutes(-1), new Price(150m), new Price(151m), new Price(149m), new Price(150.5m), new Volume(1m), new Volume(150m))],
            CancellationToken.None);

        JsonElement points = await GetAsync("/api/market/linear/SOLUSDT/open-interest?from=2024-03-01T00:00:00Z&to=2024-03-01T00:05:00Z");

        points.EnumerateArray().Select(point => (point.GetProperty("time").GetDateTimeOffset(), point.GetProperty("value").GetString())).ShouldBe([(Start, "185802453.90")]);
        _factory.Bybit.Requests.ShouldBe(0);
    }

    [Test]
    public async Task More_than_1000_points_is_an_error_by_from()
    {
        await new OpenInterestRepository(_database).UpsertAsync(Sol, [.. Enumerable.Range(0, 1001).Select(i => new OpenInterestPoint(Start.AddMinutes(5 * i), i))], CancellationToken.None);

        HttpResponseMessage response = await _client.GetAsync("/api/market/linear/SOLUSDT/open-interest?from=2024-03-01T00:00:00Z&to=2024-03-10T00:00:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty("from", out _).ShouldBeTrue();
    }

    [TestCase("open-interest")]
    [TestCase("funding")]
    public async Task Spot_pair_has_neither(string series)
    {
        (await _client.GetAsync($"/api/market/spot/SOLUSDT/{series}?from=2024-03-01T00:00:00Z&to=2024-03-02T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase("linear", "yesterday", "2024-03-02T00:00:00Z", "from")]
    [TestCase("linear", "2024-03-01T00:00:00Z", "tomorrow", "to")]
    [TestCase("inverse", "2024-03-01T00:00:00Z", "2024-03-02T00:00:00Z", "category")]
    public async Task Invalid_input_is_an_error_by_parameter(string category, string from, string to, string parameter)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/market/{category}/SOLUSDT/funding?from={from}&to={to}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty(parameter, out _).ShouldBeTrue();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        HttpResponseMessage response = await _client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}