using System.Diagnostics;
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

public class EndToEndTests
{
    [Test]
    public async Task Imported_closed_candles_are_read_back_exactly()
    {
        // The app's clock is 2024-03-10T12:30:00Z, so the 12:00 candle Bybit returns is still open.
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        factory.Bybit.Klines.AddRange(
        [
            ["2024-03-10T12:00:00Z", "42200", "42210", "42190", "42205", "0.5", "21102.5"],
            ["2024-03-10T11:00:00Z", "42100", "42300.5", "42050", "42123.45", "0.000123", "5.18"],
            ["2024-03-10T10:00:00Z", "42000.1", "42150", "41950", "42100", "12.5", "525000.75"],
            ["2024-03-10T09:00:00Z", "41900", "42010", "41890", "42000.1", "3", "126000.3"]
        ]);
        using HttpClient client = factory.CreateClient();

        JsonElement created = await (await client.PostAsJsonAsync(
            "/api/market-data/candle-imports",
            new
            {
                category = "linear",
                symbol = "BTCUSDT",
                interval = "60",
                from = "2024-03-10T09:00:00Z"
            })).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement job = await WaitUntilFinishedAsync(client, created.GetProperty("id").GetGuid());

        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        job.GetProperty("importedCandles").GetInt64().ShouldBe(3);
        List<JsonElement>? candles = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60&from=2024-03-10T00:00:00Z&to=2024-03-11T00:00:00Z");
        candles!.Select(Row)
            .ShouldBe(
                new[]
                {
                    new[] { "2024-03-10T09:00:00Z", "41900", "42010", "41890", "42000.1", "3", "126000.3" },
                    new[] { "2024-03-10T10:00:00Z", "42000.1", "42150", "41950", "42100", "12.5", "525000.75" },
                    new[] { "2024-03-10T11:00:00Z", "42100", "42300.5", "42050", "42123.45", "0.000123", "5.18" }
                });
    }

    // Scenarios "Период уже загружен целиком" and "Повторная загрузка".
    [Test]
    public async Task Import_of_a_loaded_period_asks_bybit_for_nothing_and_keeps_the_candles()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        factory.Bybit.Klines.AddRange(Hours("2024-03-10T09:00:00Z", "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z"));
        using HttpClient client = factory.CreateClient();
        await ImportAsync(client, "2024-03-10T09:00:00Z", "2024-03-10T12:00:00Z");
        List<JsonElement>? loaded = await GetCandlesAsync(client);
        factory.Bybit.KlineRequests.Clear();
        factory.Bybit.Klines.Clear();
        factory.Bybit.Klines.AddRange(Hours("2024-03-10T09:00:00Z", "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z").Select(row => row.Select(value => value == "1" ? "9" : value).ToArray()));

        JsonElement job = await ImportAsync(client, "2024-03-10T09:00:00Z", "2024-03-10T12:00:00Z");

        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        job.GetProperty("importedCandles").GetInt64().ShouldBe(0);
        factory.Bybit.KlineRequests.ShouldBeEmpty();
        (await GetCandlesAsync(client))!.Select(Row).ShouldBe(loaded!.Select(Row));
    }

    // Scenario "Период загружен частично другой задачей".
    [Test]
    public async Task Import_of_a_partly_loaded_period_asks_bybit_only_for_the_missing_ranges()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        factory.Bybit.Klines.AddRange(Hours("2024-03-10T09:00:00Z", "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z"));
        using HttpClient client = factory.CreateClient();
        await ImportAsync(client, "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z");
        factory.Bybit.KlineRequests.Clear();

        JsonElement job = await ImportAsync(client, "2024-03-10T09:00:00Z", "2024-03-10T12:00:00Z");

        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        job.GetProperty("importedCandles").GetInt64().ShouldBe(2);
        factory.Bybit.KlineRequests.ShouldBe(
        [
            (At("2024-03-10T09:00:00Z"), At("2024-03-10T09:59:59.999Z")),
            (At("2024-03-10T11:00:00Z"), At("2024-03-10T11:59:59.999Z"))
        ]);
        (await GetCandlesAsync(client))!.Count.ShouldBe(3);
    }

    // Scenario "Повтор упавшей задачи": the job had stored the 11:00 candle before Bybit became unavailable.
    [Test]
    public async Task Retried_job_loads_only_the_candles_still_missing()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync();
        factory.Bybit.Klines.AddRange(Hours("2024-03-10T09:00:00Z", "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z"));
        using HttpClient client = factory.CreateClient();
        var btcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");
        CandleImportJob failed = CandleImportJobGenerator.Generate(CandleImportStatus.Failed) with
        {
            Instrument = btcLinear,
            Interval = Interval.OneHour,
            From = At("2024-03-10T09:00:00Z"),
            To = At("2024-03-10T12:00:00Z"),
            ImportedCandles = 1,
            Error = "Bybit request failed after 5 retries: HTTP 503",
            StartedAt = factory.Time.GetUtcNow(),
            FinishedAt = factory.Time.GetUtcNow()
        };
        await using (var dataSource = NpgsqlDataSource.Create(factory.ConnectionString))
        {
            await new CandleImportJobRepository(dataSource).AddAsync(failed, CancellationToken.None);
            await new CandleRepository(dataSource).UpsertAsync(btcLinear, Interval.OneHour, [CandleGenerator.Generate(At("2024-03-10T11:00:00Z"))], CancellationToken.None);
        }

        (await client.PostAsync($"/api/market-data/candle-imports/{failed.Id}/retry", null)).EnsureSuccessStatusCode();
        JsonElement job = await WaitUntilFinishedAsync(client, failed.Id);

        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        job.GetProperty("importedCandles").GetInt64().ShouldBe(3);
        factory.Bybit.KlineRequests.ShouldBe([(At("2024-03-10T09:00:00Z"), At("2024-03-10T10:59:59.999Z"))]);
    }

    private static async Task<JsonElement> ImportAsync(HttpClient client, string from, string to)
    {
        JsonElement created = await (await client.PostAsJsonAsync(
            "/api/market-data/candle-imports",
            new { category = "linear", symbol = "BTCUSDT", interval = "60", from, to })).Content.ReadFromJsonAsync<JsonElement>();

        return await WaitUntilFinishedAsync(client, created.GetProperty("id").GetGuid());
    }

    private static Task<List<JsonElement>?> GetCandlesAsync(HttpClient client)
    {
        return client.GetFromJsonAsync<List<JsonElement>>(
            "/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60&from=2024-03-10T00:00:00Z&to=2024-03-11T00:00:00Z");
    }

    private static IEnumerable<string[]> Hours(params string[] openTimes)
    {
        return openTimes.Select(openTime => new[] { openTime, "1", "2", "0.5", "1.5", "3", "4.5" });
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }

    private static string[] Row(JsonElement candle)
    {
        return
        [
            candle.GetProperty("openTime").GetDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            .. new[] { "open", "high", "low", "close", "volume", "turnover" }.Select(name => candle.GetProperty(name).GetString()!)
        ];
    }

    private static async Task<JsonElement> WaitUntilFinishedAsync(HttpClient client, Guid id)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            JsonElement job = await client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{id}");

            if (job.GetProperty("status").GetString() is "Succeeded" or "Failed" || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return job;
            }

            await Task.Delay(50);
        }
    }
}