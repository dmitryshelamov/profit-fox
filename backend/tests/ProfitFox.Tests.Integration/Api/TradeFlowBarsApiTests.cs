using System.Globalization;
using System.Net;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Бары покупок и продаж пары" and "Начало и разрывы данных потока сделок": from storage only.</summary>
public class TradeFlowBarsApiTests
{
    private static readonly DateTimeOffset Today = DateTimeOffset.Parse("2024-03-10T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Guid Owner = Guid.NewGuid();

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = ProfitFoxFactory.Create(workers: 0);
        _client = _factory.CreateClient();
        _database = NpgsqlDataSource.Create(_factory.ConnectionString);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE trade_flow_state SET lease_owner = @Owner, leased_until = now() + INTERVAL '1 hour' WHERE id = 1", new { Owner });
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _database.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Scenario "Часовые бары": 10 days of minutes give 240 hourly bars, each the sum of its 60 minutes.
    [Test]
    public async Task Hourly_bars_of_10_days_add_up_their_minutes()
    {
        DateTimeOffset from = Today.AddDays(-10).AddHours(12);
        await new TradeFlowMinuteRepository(_database).SaveAsync(
            Owner,
            [.. Enumerable.Range(0, 14400).Select(i => (Sol, new TradeFlowMinute(from.AddMinutes(i), 1.5m, 0.25m, 150m, 25m, 2, 1)))],
            CancellationToken.None);

        JsonElement answer = await GetAsync("/api/market/linear/SOLUSDT/trade-flow?interval=60&from=2024-02-29T12:00:00Z&to=2024-03-10T12:00:00Z");

        answer.GetProperty("interval").GetString().ShouldBe("60");
        JsonElement[] bars = [.. answer.GetProperty("bars").EnumerateArray()];
        bars.Length.ShouldBe(240);
        bars.ShouldAllBe(bar => decimal.Parse(bar.GetProperty("buyVolume").GetString()!, CultureInfo.InvariantCulture) == 90m && decimal.Parse(bar.GetProperty("sellTurnover").GetString()!, CultureInfo.InvariantCulture) == 1500m && bar.GetProperty("buyTrades").GetInt32() == 120);
        bars[0].GetProperty("openTime").GetDateTimeOffset().ShouldBe(from);
        answer.GetProperty("since").GetDateTimeOffset().ShouldBe(from);
        _factory.Bybit.Requests.ShouldBe(0);
    }

    // Scenarios "Разрыв сегодня" and "Разрыв вчера закрыт".
    [Test]
    public async Task Gaps_are_the_breaks_of_the_flow_out_of_the_rebuilt_days_of_the_pair()
    {
        var breaks = new StreamBreakRepository(_database);
        await breaks.AddAsync(new StreamBreak("trades", Today.AddHours(-10), Today.AddHours(-10).AddMinutes(3)), CancellationToken.None);
        await breaks.AddAsync(new StreamBreak("trades", Today.AddHours(10).AddMinutes(2), Today.AddHours(10).AddMinutes(5)), CancellationToken.None);
        await new TradeFlowDayRepository(_database).SaveAsync(new TradeFlowDay(Sol, Today.AddDays(-1), TradeFlowDayStatus.Rebuilt, Today), CancellationToken.None);

        JsonElement answer = await GetAsync("/api/market/linear/SOLUSDT/trade-flow?interval=15&from=2024-03-09T00:00:00Z&to=2024-03-10T12:00:00Z");

        answer.GetProperty("gaps").EnumerateArray().Select(gap => (gap.GetProperty("from").GetDateTimeOffset(), gap.GetProperty("to").GetDateTimeOffset()))
            .ShouldBe([(Today.AddHours(10).AddMinutes(2), Today.AddHours(10).AddMinutes(5))]);
        answer.GetProperty("bars").GetArrayLength().ShouldBe(0);
        answer.GetProperty("since").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Scenario "Пара moex".
    [Test]
    public async Task Pair_of_moex_is_not_found()
    {
        (await _client.GetAsync("/api/market/moex/SBER/trade-flow?interval=60&from=2024-03-09T00:00:00Z&to=2024-03-10T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Слишком длинный период".
    [Test]
    public async Task Period_longer_than_1000_minutes_is_an_error_by_from()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market/linear/SOLUSDT/trade-flow?interval=1&from=2024-03-09T00:00:00Z&to=2024-03-09T16:41:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty("from", out _).ShouldBeTrue();
    }

    [Test]
    public async Task OpenApi_document_describes_the_bars()
    {
        using var document = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));

        document.RootElement.GetProperty("paths").TryGetProperty("/api/market/{category}/{symbol}/trade-flow", out _).ShouldBeTrue();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        HttpResponseMessage response = await _client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}