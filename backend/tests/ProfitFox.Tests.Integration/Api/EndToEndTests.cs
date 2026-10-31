using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Tests.Integration.Infrastructure;

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
        factory.Bybit.KlinePages.Enqueue(
        [
            ["2024-03-10T12:00:00Z", "42200", "42210", "42190", "42205", "0.5", "21102.5"],
            ["2024-03-10T11:00:00Z", "42100", "42300.5", "42050", "42123.45", "0.000123", "5.18"],
            ["2024-03-10T10:00:00Z", "42000.1", "42150", "41950", "42100", "12.5", "525000.75"],
            ["2024-03-10T09:00:00Z", "41900", "42010", "41890", "42000.1", "3", "126000.3"],
        ]);
        using HttpClient client = factory.CreateClient();

        JsonElement created = await (await client.PostAsJsonAsync("/api/market-data/candle-imports", new
        {
            category = "linear",
            symbol = "BTCUSDT",
            interval = "60",
            from = "2024-03-10T09:00:00Z",
        })).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement job = await WaitUntilFinishedAsync(client, created.GetProperty("id").GetGuid());

        job.GetProperty("status").GetString().ShouldBe("Succeeded");
        job.GetProperty("importedCandles").GetInt64().ShouldBe(3);
        List<JsonElement>? candles = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/market-data/candles?category=linear&symbol=BTCUSDT&interval=60&from=2024-03-10T00:00:00Z&to=2024-03-11T00:00:00Z");
        candles!.Select(Row).ShouldBe(new[]
        {
            new[] { "2024-03-10T09:00:00Z", "41900", "42010", "41890", "42000.1", "3", "126000.3" },
            new[] { "2024-03-10T10:00:00Z", "42000.1", "42150", "41950", "42100", "12.5", "525000.75" },
            new[] { "2024-03-10T11:00:00Z", "42100", "42300.5", "42050", "42123.45", "0.000123", "5.18" },
        });
    }

    private static string[] Row(JsonElement candle) =>
    [
        candle.GetProperty("openTime").GetDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        .. new[] { "open", "high", "low", "close", "volume", "turnover" }.Select(name => candle.GetProperty(name).GetString()!),
    ];

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
