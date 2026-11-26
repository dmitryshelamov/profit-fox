using System.Text.Json;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 7.2: a backtest of the queue on the fake market-data service — spec market-history
///     «Источник истории», «Граница периода», «Чтение через кэш» («Повторный прогон без кэша»: these services have no folder
///     of the cache), «Минутные свечи в окнах сделок» («Шесть наборов на одной истории») and spec strategy-backtest «История
///     прогона» («История после конца»).
/// </summary>
[NonParallelizable]
public sealed class ExecuteBacktestTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 1, 19, 0, 0, 0, TimeSpan.Zero);
    private MarketDataServer _server = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = null;
        await BenchDatabase.Reset();
        _server = await MarketDataServer.Start();
        _services = BenchServices.Start(_server);
    }

    [TearDown]
    public async Task Stop()
    {
        await _services.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Test]
    public async Task A_backtest_reads_nothing_after_the_end_of_its_history()
    {
        // The fake service has history after the end: every read stops at it, the payment at the end included.
        BacktestOutcome outcome = await Run(Generate());

        Assert.That(outcome.Sets.Single().Numbers.Trades, Is.GreaterThan(0));
        Assert.That(_server.Data.CandleRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(End));
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(End.AddSeconds(1)));
        Assert.That(_server.Data.CandleRequests, Is.Not.Empty);
    }

    /// <summary>
    ///     «Шесть наборов на одной истории» (change fix-backtest-random-control, task 4.2): the candles of the timeframe and
    ///     the funding are read twice, when the orders are found and when the trades are computed; the minutes of each week
    ///     once, and none of a week without orders and without random entries.
    /// </summary>
    [Test]
    public async Task Six_sets_on_one_timeframe_read_the_candles_twice_and_the_minutes_of_each_week_of_a_pair_once()
    {
        await Run(Generate(thresholds: ["0.0003", "0.0004", "0.0005", "0.00055", "0.0006", "0.001"]));

        CandleHistoryRequest[] btc = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "btc")];
        Assert.That(btc.Count(request => request.Interval == CandleInterval.FourHours), Is.EqualTo(2));
        CandleHistoryRequest[] minutes = [.. btc.Where(request => request.Interval != CandleInterval.FourHours)];
        Assert.That(minutes.Select(request => request.From), Is.Unique, "each week once for the six sets");
        Assert.That(minutes, Has.Length.EqualTo(2), "two weeks of history have orders");
        Assert.That(_server.Data.FundingRequests.Count(request => request.InstrumentIds.Single() == "btc"), Is.EqualTo(2));
    }

    [Test]
    public async Task A_second_backtest_reads_the_history_again()
    {
        await Run(Generate());
        int first = _server.Data.CandleRequests.Count;

        await Run(Generate());

        Assert.That(_server.Data.CandleRequests, Has.Count.EqualTo(2 * first), "the bench keeps no copy of history");
    }

    [Test]
    public async Task A_backtest_whose_service_does_not_answer_fails_with_history_unavailable()
    {
        _server.Data.Failure = StatusCode.Unavailable;
        Backtest backtest = await Claimed(Generate());

        Assert.That(
            () => _services.GetRequiredService<ISender>().Send(new ExecuteBacktestCommand(backtest, new RunAssignment(backtest.Id, "w-1"))),
            Throws.Exception);

        Backtest failed = (await _services.GetRequiredService<IBacktestRepository>().FindAsync(backtest.Id, CancellationToken.None))!;
        Assert.That(failed.Status, Is.EqualTo(BacktestState.Failed));
        Assert.That(failed.Reason, Does.StartWith("история недоступна"));
        Assert.That(failed.Sets.Single().Numbers, Is.Null, "a failed backtest has no result");
    }

    /// <summary>
    ///     Task 12.12: a read the service dropped is repeated from scratch. The items of the broken stream are thrown away, so
    ///     the backtest gives the numbers and the data hash of one whose reads were not dropped.
    /// </summary>
    [Test]
    public async Task A_backtest_whose_first_two_reads_were_dropped_is_done_with_the_numbers_and_the_hash_of_one_without_drops()
    {
        BacktestOutcome whole = await Run(Generate());
        int requests = _server.Data.CandleRequests.Count + _server.Data.FundingRequests.Count;
        _server.Data.CandleRequests.Clear();
        _server.Data.FundingRequests.Clear();
        _server.Data.Drops = 2;

        BacktestOutcome repeated = await Run(Generate());

        Assert.That(_server.Data.Drops, Is.Zero, "both drops were met");
        Assert.That(_server.Data.CandleRequests.Count + _server.Data.FundingRequests.Count, Is.EqualTo(requests + 2), "each dropped read once more");
        Assert.That(repeated.DataHash, Is.EqualTo(whole.DataHash));
        Assert.That(Numbers(repeated), Is.EqualTo(Numbers(whole)));
    }

    /// <summary>
    ///     Task 12.12: when the three repeats are used up the backtest fails with history unavailable.
    /// </summary>
    [Test]
    public async Task A_backtest_whose_reads_are_always_dropped_fails_with_history_unavailable_after_three_repeats()
    {
        _server.Data.Drops = int.MaxValue;

        Backtest failed = await Execute(Generate(), TimeProvider.System);

        Assert.That(failed.Status, Is.EqualTo(BacktestState.Failed));
        Assert.That(failed.Reason, Does.StartWith("история недоступна"));
        Assert.That(failed.Sets.Single().Numbers, Is.Null, "a failed backtest has no result");
        Assert.That(_server.Data.CandleRequests, Has.Count.EqualTo(4), "the first read and three repeats");
        Assert.That(_server.Data.FundingRequests, Is.Empty, "nothing is read after the read that failed");
    }

    /// <summary>
    ///     Task 12.12: a window the service refuses is not repeated: the answer would be the same.
    /// </summary>
    [Test]
    public async Task A_backtest_whose_window_the_service_refuses_fails_with_history_unavailable_after_one_request()
    {
        _server.Data.Failure = StatusCode.InvalidArgument;

        Backtest failed = await Execute(Generate(), TimeProvider.System);

        Assert.That(failed.Status, Is.EqualTo(BacktestState.Failed));
        Assert.That(failed.Reason, Does.StartWith("история недоступна"));
        Assert.That(_server.Data.CandleRequests, Has.Count.EqualTo(1));
    }

    /// <summary>
    ///     Task 12.1: the market-data service holds past days only. With the end of history at the start of the current day the
    ///     funding is read up to it and not past it; a trade that ends exactly there is left out on funding, because the
    ///     payment at its end is not known yet, and every trade that ends before it is as with a later clock.
    /// </summary>
    [Test]
    public async Task A_backtest_up_to_today_reads_no_funding_of_today_and_leaves_the_trade_that_ends_there_out_on_funding()
    {
        // The strategy enters at 16:00 every day for 8 hours: its trade of the last day ends exactly at the end of history.
        // The payment of 08:00 belongs to the candle closed after it (task 27.3), and the 4h candle of 08:00 has no trades.
        Backtest later = await Execute(Generate(after: "8h"), new FixedTime(End.AddDays(1).AddHours(9)));
        _server.Data.FundingRequests.Clear();

        Backtest today = await Execute(Generate(after: "8h"), new FixedTime(End.AddHours(9)));

        Assert.That(today.Status, Is.EqualTo(BacktestState.Done), today.Reason);
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(End));
        SetSummary before = later.Sets.Single().Numbers!;
        SetSummary now = today.Sets.Single().Numbers!;
        Assert.That(before.Excluded.Funding, Is.Zero, "with a later clock the payment at the end is known");
        Assert.That(now.Excluded.Funding, Is.EqualTo(3), "the trade of the last day of each of the three pairs");
        Assert.That(now.Trades, Is.EqualTo(before.Trades - 3));
    }

    /// <summary>
    ///     Task 12.13: the current day of a backtest is taken once, when it starts. A backtest up to today whose clock passes
    ///     midnight UTC after its first pair treats every pair alike — the trade that ends at the end of history is left out on
    ///     funding for all three — and gives the numbers of the same backtest on a clock that stays before midnight.
    /// </summary>
    [Test]
    public async Task A_backtest_up_to_today_that_runs_across_midnight_treats_every_pair_as_before_midnight()
    {
        Backtest before = await Execute(Generate(after: "8h"), new FixedTime(End.AddHours(23).AddMinutes(59)));
        var clock = new MidnightClock(End.AddHours(23).AddMinutes(59), End.AddDays(1).AddMinutes(1));
        _server.Data.BeforeCandles = (request, _) =>
        {
            if (request.InstrumentIds.Single() != "btc")
            {
                clock.Pass();
            }

            return Task.CompletedTask;
        };

        Backtest across = await Execute(Generate(after: "8h"), clock);

        Assert.That(clock.Passed, Is.True, "the clock passed midnight between the pairs");
        Assert.That(across.Status, Is.EqualTo(BacktestState.Done), across.Reason);
        Assert.That(across.Sets.Single().Numbers!.Excluded.Funding, Is.EqualTo(3), "the trade of the last day of each of the three pairs");
        Assert.That(JsonSerializer.Serialize(across.Sets.Single().Numbers), Is.EqualTo(JsonSerializer.Serialize(before.Sets.Single().Numbers)));
        Assert.That(across.DataHash, Is.EqualTo(before.DataHash));
    }

    // A clock before midnight until it is passed, after midnight since.
    /// <summary>
    ///     Change add-ideas-and-market, task 4.3: spec market-history «Свечи названной пары» — «Одно чтение на прогон» and
    ///     «Хэш данных».
    /// </summary>
    [Test]
    public async Task The_candles_of_a_named_pair_are_read_once_a_backtest_and_go_into_its_hash()
    {
        BacktestOutcome first = await Run(Named(["ETHUSDT", "SOLUSDT"]));

        CandleHistoryRequest[] btc = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "btc")];
        Assert.That(btc.Select(request => request.Interval), Is.EqualTo(new[] { CandleInterval.FourHours }), "once for the run, not for each of the two pairs");
        Assert.That(first.Sets.Single().Numbers.Trades, Is.GreaterThan(0), "the node gives the candles of BTCUSDT");

        _server.Data.Closes["btc"] = "100.75";
        BacktestOutcome second = await Run(Named(["ETHUSDT", "SOLUSDT"]));

        Assert.That(second.DataHash, Is.Not.EqualTo(first.DataHash), "the history of BTCUSDT changed");
        Assert.That(Numbers(second), Is.EqualTo(Numbers(first)), "the close is still above zero");
    }

    // funding-short whose entry also asks the close of BTCUSDT to be above zero, on the given pairs.
    private static Backtest Named(IReadOnlyList<string> pairs)
    {
        Backtest backtest = Generate();
        string json = BacktestGenerator.FundingShort
            .Replace(
                """{ "id": "first", "kind": "first-time" },""",
                """{ "id": "first", "kind": "first-time" }, { "id": "btc", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } }, { "id": "up", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0 } }, { "id": "both", "kind": "and" },""",
                StringComparison.Ordinal)
            .Replace(
                """{ "from": "high.result", "to": "first.value" },""",
                """{ "from": "high.result", "to": "both.a" }, { "from": "btc.close", "to": "up.a" }, { "from": "up.result", "to": "both.b" }, { "from": "both.result", "to": "first.value" },""",
                StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        return new Backtest
        {
            Id = backtest.Id,
            StrategyId = backtest.StrategyId,
            StrategyName = backtest.StrategyName,
            StrategyVersion = backtest.StrategyVersion,
            Kind = backtest.Kind,
            Graph = StrategyGraph.ReadForm(document.RootElement).Value!,
            Settings = backtest.Settings with { Pairs = pairs },
            Sets = backtest.Sets,
            SetCount = backtest.SetCount
        };
    }

    private sealed class MidnightClock(DateTimeOffset before, DateTimeOffset after) : TimeProvider
    {
        private int _passed;

        public bool Passed => Volatile.Read(ref _passed) == 1;

        public void Pass()
        {
            Volatile.Write(ref _passed, 1);
        }

        public override DateTimeOffset GetUtcNow()
        {
            return Passed ? after : before;
        }
    }

    private async Task<Backtest> Execute(Backtest backtest, TimeProvider time)
    {
        await using ServiceProvider services = BenchServices.Start(_server, time: time);
        IBacktestRepository backtests = services.GetRequiredService<IBacktestRepository>();
        await backtests.EnqueueAsync(backtest, CancellationToken.None);
        Backtest claimed = (await backtests.ClaimAsync("w-1", Lease, CancellationToken.None))!;
        try
        {
            await services.GetRequiredService<ISender>().Send(new ExecuteBacktestCommand(claimed, new RunAssignment(claimed.Id, "w-1")));
        }
        catch (Exception exception) when (exception is not AssertionException)
        {
            // The backtest is failed with its reason; the test reads it.
        }

        return (await backtests.FindAsync(claimed.Id, CancellationToken.None))!;
    }

    private static Backtest Generate(IReadOnlyList<string>? thresholds = null, string? after = null)
    {
        Backtest backtest = BacktestGenerator.Generate(thresholds: thresholds);
        BacktestSettings settings = backtest.Settings with { End = End };
        return new Backtest
        {
            Id = backtest.Id,
            StrategyId = backtest.StrategyId,
            StrategyName = backtest.StrategyName,
            StrategyVersion = backtest.StrategyVersion,
            Kind = backtest.Kind,
            Graph = after is null ? backtest.Graph : Graph(after),
            Settings = settings,
            Sets = backtest.Sets,
            SetCount = backtest.SetCount
        };
    }

    // The graph of funding-short with the time of close-after.
    private static GraphDefinition Graph(string after)
    {
        using var document = JsonDocument.Parse(BacktestGenerator.FundingShort.Replace("\"after\": \"4h\"", $"\"after\": \"{after}\"", StringComparison.Ordinal));
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }

    // The numbers of the sets and of their pairs as text: equal text, equal numbers.
    private static string Numbers(BacktestOutcome outcome)
    {
        return JsonSerializer.Serialize(outcome.Sets.Select(set => new { set.Set, set.Numbers, set.Pairs }));
    }

    private async Task<Backtest> Claimed(Backtest backtest)
    {
        IBacktestRepository backtests = _services.GetRequiredService<IBacktestRepository>();
        await backtests.EnqueueAsync(backtest, CancellationToken.None);
        return (await backtests.ClaimAsync("w-1", Lease, CancellationToken.None))!;
    }

    private async Task<BacktestOutcome> Run(Backtest backtest)
    {
        Backtest claimed = await Claimed(backtest);
        ExecuteBacktestResult result = await _services.GetRequiredService<ISender>().Send(new ExecuteBacktestCommand(claimed, new RunAssignment(claimed.Id, "w-1")));
        Assert.That((await _services.GetRequiredService<IBacktestRepository>().FindAsync(claimed.Id, CancellationToken.None))!.Status, Is.EqualTo(BacktestState.Done));
        return result.Outcome;
    }
}