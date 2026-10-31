using System.Diagnostics;

using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class RestartTests
{
    private static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");

    // Scenario "Перезапуск во время загрузки": the job had stored the 11:00 candle when the service stopped.
    [Test]
    public async Task Job_running_when_the_service_stopped_loads_only_the_missing_candles_after_start()
    {
        string connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        CandleImportJob running = await StartAndLeaveRunningJobAsync(connectionString);

        await using var restarted = new ProfitFoxFactory(connectionString);
        restarted.Bybit.Klines.AddRange(
        [
            ["2024-03-10T11:00:00Z", "3", "3", "3", "3", "1", "3"],
            ["2024-03-10T10:00:00Z", "2", "2", "2", "2", "1", "2"],
            ["2024-03-10T09:00:00Z", "1", "1", "1", "1", "1", "1"]
        ]);
        restarted.CreateClient().Dispose();

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        CandleImportJob job = await WaitUntilFinishedAsync(new CandleImportJobRepository(dataSource), running.Id);
        job.Status.ShouldBe(CandleImportStatus.Succeeded);
        job.ImportedCandles.ShouldBe(running.ImportedCandles + 2);
        restarted.Bybit.KlineRequests.ShouldBe([(At("2024-03-10T09:00:00Z"), At("2024-03-10T10:59:59.999Z"))]);
        IReadOnlyList<DateTimeOffset> stored = await new CandleRepository(dataSource)
            .GetOpenTimesAsync(BtcLinear, Interval.OneHour, running.From, running.To, CancellationToken.None);
        stored.ShouldBe([At("2024-03-10T09:00:00Z"), At("2024-03-10T10:00:00Z"), At("2024-03-10T11:00:00Z")]);
    }

    private static async Task<CandleImportJob> StartAndLeaveRunningJobAsync(string connectionString)
    {
        await using var factory = new ProfitFoxFactory(connectionString);
        factory.CreateClient().Dispose();
        CandleImportJob job = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with
        {
            Instrument = BtcLinear,
            Interval = Interval.OneHour,
            From = At("2024-03-10T09:00:00Z"),
            To = At("2024-03-10T12:00:00Z"),
            ImportedCandles = 1,
            StartedAt = factory.Time.GetUtcNow()
        };
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new CandleImportJobRepository(dataSource).AddAsync(job, CancellationToken.None);
        await new CandleRepository(dataSource).UpsertAsync(BtcLinear, Interval.OneHour, [CandleGenerator.Generate(At("2024-03-10T11:00:00Z"))], CancellationToken.None);

        return job;
    }

    private static async Task<CandleImportJob> WaitUntilFinishedAsync(CandleImportJobRepository jobs, Guid id)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            CandleImportJob job = (await jobs.GetAsync(id, CancellationToken.None))!;

            if (job.Status is CandleImportStatus.Succeeded or CandleImportStatus.Failed || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return job;
            }

            await Task.Delay(50);
        }
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }
}