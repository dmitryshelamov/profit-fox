using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Tests.Integration.Api;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>Requirement "Фоновую работу выполняет Worker": the API only saves work, a Worker runs it.</summary>
public class WorkerAvailabilityTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2024-03-01T00:00:00Z");

    // Scenarios "Без Worker'а" and "Worker запустили позже".
    [Test]
    public async Task Backtest_created_without_a_worker_stays_pending_until_a_worker_starts()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(From, 100m, 98m, 97m, 99m);
        factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.01", "0.001", "0.001", "1");
        HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/bots/{scenario.BotId}/backtests",
            new { from = "2024-03-01T00:00:00Z", to = "2024-03-01T04:00:00Z" });
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(200);
        Backtest waiting = await scenario.GetAsync(id);
        await using WorkerHost worker = factory.StartWorker();
        Backtest finished = await scenario.WaitUntilFinishedAsync(id);

        (waiting.Status, waiting.CalculatedCombinations).ShouldBe((BacktestStatus.Pending, 0L));
        finished.Status.ShouldBe(BacktestStatus.Succeeded);
    }

    [Test]
    public async Task Import_job_created_without_a_worker_stays_pending_until_a_worker_starts()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/market-data/candle-imports",
            new { category = "linear", symbol = "BTCUSDT", interval = "60", from = "2024-03-10T09:00:00Z", to = "2024-03-10T12:00:00Z" });
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(200);
        string? waiting = (await client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{id}")).GetProperty("status").GetString();
        await using WorkerHost worker = factory.StartWorker();
        string? finished = await WaitForImportAsync(factory, client, id);

        (waiting, finished).ShouldBe(("Pending", "Succeeded"));
    }

    private static async Task<string?> WaitForImportAsync(ProfitFoxFactory factory, HttpClient client, Guid id)
    {
        for (var second = 0; second < 300; second++)
        {
            string? status = (await client.GetFromJsonAsync<JsonElement>($"/api/market-data/candle-imports/{id}")).GetProperty("status").GetString();

            if (status is "Succeeded" or "Failed")
            {
                return status;
            }

            factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        return null;
    }
}