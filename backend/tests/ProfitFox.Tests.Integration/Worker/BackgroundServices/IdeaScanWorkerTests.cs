using System.Globalization;
using System.Text.Json;

using Dapper;

using Google.Protobuf.WellKnownTypes;

using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices;

/// <summary>
///     Change add-ideas-and-market, tasks 8.1 and 8.5: the scanner of ideas in Workers of the test process on the live feeds
///     of the fake market-data service — spec market-live-feed «Один держатель живых данных» and spec strategy-ideas «Итог
///     идеи» («Выплата в момент выхода») and «Перезапуск и простой». The scanner runs on a fake clock the test moves on; the
///     lease of the scanner is timed by the database.
/// </summary>
[NonParallelizable]
public sealed class IdeaScanWorkerTests
{
    private readonly List<WorkerHost> _workers = [];
    private MarketDataServer _server = null!;

    [SetUp]
    public async Task CreateBench()
    {
        await BenchDatabase.Reset();
        _server = await MarketDataServer.Start();
    }

    [TearDown]
    public async Task StopWorkers()
    {
        foreach (WorkerHost worker in _workers)
        {
            await worker.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    /// <summary>
    ///     Spec market-live-feed «Два Worker'а» and «Worker упал».
    /// </summary>
    [Test]
    public async Task One_of_two_Workers_keeps_the_live_data_and_the_other_takes_it_over_when_it_stops()
    {
        WorkerHost first = await Start("w-1", time: null);
        await Poll.Until("the lease of the scanner", async () => await Holder() == "w-1");
        await Start("w-2", time: null);
        await Task.Delay(WorkerHost.Lease * 2);

        Assert.That(await Holder(), Is.EqualTo("w-1"), "the lease is renewed by its holder");
        Assert.That(_server.Data.Minutes.Readers, Is.EqualTo(1), "one subscription of the live data");

        _workers.Remove(first);
        await first.DisposeAsync();
        TimeSpan taken = await Poll.Until("the second Worker", async () => await Holder() == "w-2");

        Assert.That(taken, Is.LessThanOrEqualTo(WorkerHost.Lease + WorkerHost.Renewal + TimeSpan.FromSeconds(1)));
        await Poll.Until("the subscription of the second Worker", () => Task.FromResult(_server.Data.Minutes.Readers == 1));
    }

    /// <summary>
    ///     Spec market-live-feed «Прогон рядом»: the Worker that keeps the live data writes the snapshot of the market while it
    ///     computes a backtest.
    /// </summary>
    [Test]
    public async Task The_Worker_that_keeps_the_live_data_goes_on_with_it_while_it_computes_a_backtest()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(7);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Data.BeforeCandles = async (_, cancellation) =>
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(cancellation);
        };
        try
        {
            await Start("w-1", time: null);
            await Poll.Until("the lease of the scanner", async () => await Holder() == "w-1");
            await BenchServices.Get<IBacktestRepository>().EnqueueAsync(ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests.BacktestGenerator.Generate(), CancellationToken.None);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset minute = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerMinute), TimeSpan.Zero).AddMinutes(-1);
            _server.Data.Minutes.Push(FakeLiveFeedTests.Minute("sol", minute, close: "238.42"));

            await Poll.Until("the snapshot of SOLUSDT", async () => await Scalar<decimal?>("SELECT price FROM market_pairs WHERE symbol = 'SOLUSDT'") == 238.42m);
            Assert.That(await Scalar<string>("SELECT status FROM backtests"), Is.EqualTo("running"), "the backtest goes on beside");
            Assert.That(await Holder(), Is.EqualTo("w-1"));
        }
        finally
        {
            release.TrySetResult();
            FakeMarketData.CoveredUntil = null;
        }
    }

    /// <summary>
    ///     Spec strategy-ideas «Выплата в момент выхода»: the outcome is written after the payment at the exit came, with it.
    /// </summary>
    [Test]
    public async Task An_idea_that_ends_by_its_time_waits_for_the_payment_at_the_exit_and_counts_it()
    {
        (FakeTimeProvider time, DateTimeOffset start) = Clock();
        await Enable("close-up", "5m", stop: null);
        await Start("w-1", time);
        await Warmed(time);

        DateTimeOffset idea = await Idea(time, start);
        await Push(idea, 5, close: 101);
        _server.Data.Payments.Push(new FundingPaymentUpdate { InstrumentId = "btc", Time = Timestamp.FromDateTimeOffset(idea.AddMinutes(5)), FundingRate = "0.001" });
        await Push(idea.AddMinutes(5), 1, close: 101);
        await Until(time, idea.AddMinutes(5).AddSeconds(30));

        Assert.That(await Scalar<string>("SELECT state FROM ideas"), Is.EqualTo("open"), "the outcome waits 90 s after the exit");
        await Until(time, idea.AddMinutes(5).AddSeconds(95), () => Scalar<string>("SELECT state FROM ideas"), "closed");

        Assert.That(await Scalar<string>("SELECT exit_reason FROM ideas"), Is.EqualTo("time"));
        Assert.That(await Scalar<double>("SELECT funding_paid FROM ideas"), Is.EqualTo(0.001 * 101).Within(1e-12), "the payment at the exit, at the close before it");
    }

    /// <summary>
    ///     Spec strategy-ideas «Перезапуск и простой»: «Стоп во время простоя» and «Свеча закрылась в простое».
    /// </summary>
    [Test]
    public async Task After_a_downtime_the_idea_closes_by_the_stop_of_the_downtime_and_its_candles_give_no_ideas()
    {
        (FakeTimeProvider time, DateTimeOffset start) = Clock();
        await Enable("close-up", "1h", stop: 0.02);
        WorkerHost first = await Start("w-1", time);
        await Warmed(time);
        DateTimeOffset idea = await Idea(time, start);
        await Push(idea, 1, close: 101);
        await Until(time, idea.AddMinutes(1).AddSeconds(5), () => Scalar<string>("SELECT entry_price IS NOT NULL FROM ideas"), "True");
        _workers.Remove(first);
        await first.DisposeAsync();

        // The downtime: the stop is hit at minute 4; the candle of 5 minutes after the idea falls, the next one rises, which
        // would be an order.
        for (int minute = 1; minute < 10; minute++)
        {
            decimal close = minute < 5 ? 99 : 101;
            CandleUpdate update = FakeLiveFeedTests.Minute("btc", idea.AddMinutes(minute), close: close.ToString(System.Globalization.CultureInfo.InvariantCulture));
            update.Low = minute == 4 ? "97" : update.Low;
            _server.Data.Minutes.Store(update);
        }

        time.SetUtcNow(idea.AddMinutes(12));
        await Start("w-2", time);
        await Until(time, idea.AddMinutes(15), () => Scalar<string>("SELECT state FROM ideas"), "closed");

        Assert.That(await Scalar<int>("SELECT count(*) FROM ideas"), Is.EqualTo(1), "no idea of the candle that closed in the downtime");
        Assert.That(await Scalar<string>("SELECT exit_reason FROM ideas"), Is.EqualTo("stop"));
        Assert.That(await Scalar<DateTime>("SELECT exit_time FROM ideas"), Is.EqualTo(idea.AddMinutes(4).UtcDateTime));
    }

    // A fake clock 30 minutes before the last whole five minutes of now: within the day the fake feed catches up.
    private static (FakeTimeProvider Time, DateTimeOffset Start) Clock()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset start = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.FromMinutes(5).Ticks), TimeSpan.Zero).AddMinutes(-30);
        return (new FakeTimeProvider(start.AddSeconds(5)), start);
    }

    private async Task<WorkerHost> Start(string name, TimeProvider? time)
    {
        WorkerHost worker = await WorkerHost.Start(_server, name, scanner: true, time: time);
        _workers.Add(worker);
        return worker;
    }

    // close-up on BTCUSDT 5m: long the first time the close is above 100, for after, with a stop; the history of the warm-up
    // closes at 99.
    private async Task Enable(string name, string after, double? stop)
    {
        _server.Data.Closes["btc"] = "99";
        _server.Data.Closes["eth"] = "99";
        _server.Data.Closes["sol"] = "99";
        string stopNode = stop is { } distance ? $$""", { "id": "stop-loss", "kind": "stop-loss", "inputs": { "distance": {{distance.ToString(CultureInfo.InvariantCulture)}} } }""" : "";
        string stopWires = stop is null ? "" : """, { "from": "position.position", "to": "stop-loss.position" }, { "from": "stop-loss.order", "to": "position.orders" }""";
        string json = $$"""
                        {
                          "formatVersion": 1,
                          "nodes": [
                            { "id": "c", "kind": "candle" },
                            { "id": "up", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 100 } },
                            { "id": "first", "kind": "first-time" },
                            { "id": "enter", "kind": "enter", "settings": { "side": "long" }, "inputs": { "size": 100 } },
                            { "id": "position", "kind": "position" },
                            { "id": "close-after", "kind": "close-after", "inputs": { "after": "{{after}}" } }{{stopNode}}
                          ],
                          "wires": [
                            { "from": "c.close", "to": "up.a" },
                            { "from": "up.result", "to": "first.value" },
                            { "from": "first.event", "to": "enter.when" },
                            { "from": "enter.order", "to": "position.orders" },
                            { "from": "position.position", "to": "close-after.position" },
                            { "from": "close-after.order", "to": "position.orders" }{{stopWires}}
                          ]
                        }
                        """;
        using var document = JsonDocument.Parse(json);
        StrategyDocument strategy = await BenchServices.Get<IStrategyRepository>().CreateAsync(
            StrategyDocument.Create(name, null, StrategyGraph.ReadForm(document.RootElement).Value!, new Dictionary<string, NodePosition>(), StrategyKind.Idea),
            CancellationToken.None);
        await BenchServices.Get<IStrategyIdeasRepository>().SetAsync(new StrategyIdeas(strategy.Id, true, [TimeSpan.FromMinutes(5)]), CancellationToken.None);
    }

    // The scanner has warmed the three pairs up.
    private static async Task Warmed(FakeTimeProvider time)
    {
        await Poll.Until("the warm-up", async () =>
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            return await Scalar<int>("SELECT COALESCE((SELECT warmed FROM scanner_lease), 0)") == 3;
        });
    }

    // The minutes of BTCUSDT of the five minutes after the start rise above 100: an idea at their close.
    private async Task<DateTimeOffset> Idea(FakeTimeProvider time, DateTimeOffset start)
    {
        DateTimeOffset close = start.AddMinutes(5);
        await Push(start, 5, close: 101);
        await Until(time, close.AddSeconds(15), async () => (await Scalar<int>("SELECT count(*) FROM ideas")).ToString(System.Globalization.CultureInfo.InvariantCulture), "1");
        return close;
    }

    private Task Push(DateTimeOffset from, int count, decimal close)
    {
        for (int minute = 0; minute < count; minute++)
        {
            _server.Data.Minutes.Push(FakeLiveFeedTests.Minute("btc", from.AddMinutes(minute), close: close.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return Task.CompletedTask;
    }

    // Moves the clock on to the moment in small steps, then until the value is there.
    private static async Task Until(FakeTimeProvider time, DateTimeOffset moment, Func<Task<string?>>? value = null, string? expected = null)
    {
        while (time.GetUtcNow() < moment)
        {
            TimeSpan step = moment - time.GetUtcNow() < TimeSpan.FromSeconds(5) ? moment - time.GetUtcNow() : TimeSpan.FromSeconds(5);
            time.Advance(step);
            await Task.Delay(20);
        }

        if (value is not null)
        {
            await Poll.Until($"the value {expected}", async () =>
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                return await value() == expected;
            });
        }
    }

    private static async Task<string?> Holder()
    {
        return await Scalar<string?>("SELECT worker FROM scanner_lease WHERE until > now()");
    }

    private static async Task<T?> Scalar<T>(string sql)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql);
    }
}
