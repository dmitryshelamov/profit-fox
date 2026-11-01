using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NSubstitute;

using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Api;
using ProfitFox.Tests.Integration.Infrastructure;

using Serilog.Core;
using Serilog.Events;

using Shouldly;

using WorkerProgram = ProfitFox.Worker.Program;

namespace ProfitFox.Tests.Integration.Logging;

/// <summary>Requirements "Лог Worker'а" and "Настройки Worker'а".</summary>
public class WorkerLoggingTests
{
    // Scenarios "Продление не короче аренды" and "Ошибка запуска Worker'а". Tests run one at a time, so redirecting the console is safe.
    [Test]
    public async Task Worker_whose_lease_would_expire_before_its_renewal_does_not_start_and_logs_why_as_fatal()
    {
        var console = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(console);

        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => WorkerProgram.Main(
            [
                "--contentRoot", WorkerHost.ContentRoot(),
                "--environment", "Production",
                "--Worker:LeaseDuration", "00:00:30",
                "--Worker:LeaseRenewal", "00:00:30"
            ]));
        }
        finally
        {
            Console.SetOut(original);
        }

        string line = console.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ShouldHaveSingleItem();
        using var logEvent = JsonDocument.Parse(line);
        logEvent.RootElement.GetProperty("@l").GetString().ShouldBe("Fatal");
        logEvent.RootElement.GetProperty("@x").GetString()!.ShouldContain("Worker:LeaseRenewal");
    }

    // Scenario "Сбой куска в Production": the failure is logged with the backtest, and the Worker takes the chunk again later.
    [Test]
    public async Task Failure_of_a_chunk_is_logged_with_its_backtest_and_the_worker_goes_on()
    {
        await using ProfitFoxFactory factory = await ProfitFoxFactory.CreateAsync(workers: 0);
        using HttpClient client = factory.CreateClient();
        var scenario = new BacktestScenario(factory, client);
        await scenario.CreateBotAsync("ladder");
        await scenario.StoreCandlesAsync(DateTimeOffset.Parse("2024-03-01T00:00:00Z"), 100m, 98m, 97m, 99m);
        Backtest backtest = await scenario.CreateAsync(DateTimeOffset.Parse("2024-03-01T00:00:00Z"), DateTimeOffset.Parse("2024-03-01T04:00:00Z"));
        ILogEventSink sink = Substitute.For<ILogEventSink>();

        var reads = new StrongBox<int>();
        await using WorkerHost worker = factory.StartWorker(services => services.AddScoped(provider => FailingOnceToReadCandles(provider, reads)), sink);
        Backtest finished = await scenario.WaitUntilFinishedAsync(backtest.Id);

        LogEvent failure = sink.ReceivedCalls().Select(call => (LogEvent)call.GetArguments()[0]!).Where(e => e.Level == LogEventLevel.Error).ShouldHaveSingleItem();
        failure.Properties["BacktestId"].ToString().ShouldBe(backtest.Id.ToString());
        failure.Exception!.Message.ShouldBe("The database is down.");
        finished.Status.ShouldBe(BacktestStatus.Succeeded);
    }

    /// <summary>The stored candles, except that the first read of a period's candles of the Worker fails; <paramref name="reads" /> counts them.</summary>
    private static ICandleRepository FailingOnceToReadCandles(IServiceProvider provider, StrongBox<int> reads)
    {
        var stored = new CandleRepository(provider.GetRequiredService<NpgsqlDataSource>());
        ICandleRepository candles = Substitute.For<ICandleRepository>();
        candles.GetOpenTimesAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => stored.GetOpenTimesAsync(call.ArgAt<Instrument>(0)!, call.ArgAt<Interval>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3), call.ArgAt<CancellationToken>(4)));
        candles.GetAsync(Arg.Any<Instrument>(), Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Interlocked.Increment(ref reads.Value) == 1
                ? throw new InvalidOperationException("The database is down.")
                : stored.GetAsync(call.ArgAt<Instrument>(0)!, call.ArgAt<Interval>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3), call.ArgAt<int>(4), call.ArgAt<CancellationToken>(5)));

        return candles;
    }
}