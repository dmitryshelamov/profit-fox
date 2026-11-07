using System.Globalization;
using System.Net;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Свечи пары".</summary>
public class MarketCandlesApiTests
{
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        // The clock reads 2024-03-10T12:30Z, so the last closed hour is 11:00.
        _factory = ProfitFoxFactory.Create(workers: 0);
        _client = _factory.CreateClient();
        var start = DateTimeOffset.Parse("2024-02-29T12:00:00Z", CultureInfo.InvariantCulture);

        for (var hour = 0; hour < 240; hour++)
        {
            _factory.Bybit.Klines.Add([start.AddHours(hour).ToString("O", CultureInfo.InvariantCulture), "150", "151", "149", "150.5", "7.5", "1125"]);
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Часовые свечи пары".
    [Test]
    public async Task Hourly_candles_of_ten_days_come_from_bybit_and_are_stored()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market/linear/SOLUSDT/candles?interval=60&from=2024-02-29T12:00:00Z&to=2024-03-10T12:00:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("interval").GetString().ShouldBe("60");
        body.GetProperty("unavailable").GetBoolean().ShouldBeFalse();
        body.GetProperty("candles").GetArrayLength().ShouldBe(240);
        body.GetProperty("candles")[0].GetProperty("close").GetString().ShouldBe("150.5");
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM candles WHERE category = 'linear' AND symbol = 'SOLUSDT' AND \"interval\" = '60'")).ShouldBe(240);
    }

    [Test]
    public async Task Period_longer_than_1000_candles_is_an_error_by_from()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/market/linear/SOLUSDT/candles?interval=1&from=2024-03-08T12:00:00Z&to=2024-03-10T12:00:00Z");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").TryGetProperty("from", out _).ShouldBeTrue();
    }
}