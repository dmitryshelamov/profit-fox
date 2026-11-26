using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     Change fix-backtest-random-control, tasks 4.1–4.4: a backtest of funding-short in two passes on the fake market-data
///     service over two weeks from 2026-01-05 — spec strategy-backtest «Случайные входы», «Повторяемость прогона» and spec
///     market-history «Минутные свечи в окнах сделок». The first candles close on 2026-01-05 04:00, so the control group of
///     the first week is empty and that of the second has the three pairs. By default the fake pays every pair the same
///     funding, so the three pairs have their orders at the same moments and take no random entries; with the high payments
///     moved apart (<see cref="FakeMarketData.HighPayments" />) each order of a pair takes random entries on the other two.
/// </summary>
[NonParallelizable]
public sealed class ExecuteBacktestPassesTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 1, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondWeek = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] Six = ["0.0003", "0.0004", "0.0005", "0.00055", "0.0006", "0.001"];
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

    /// <summary>
    ///     The trades of the orders do not change: the numbers of the sets and of their pairs but the difference are those
    ///     engine version 4 gave on the same data, recorded from the code before the change in
    ///     <c>Fixtures/EngineVersion4Numbers.json</c>.
    /// </summary>
    [Test]
    public async Task The_numbers_of_the_trades_of_the_sets_are_those_of_engine_version_4()
    {
        BacktestOutcome one = await Run(Generate());
        BacktestOutcome six = await Run(Generate(Six));

        Version4Numbers recorded = JsonSerializer.Deserialize<Version4Numbers>(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "EngineVersion4Numbers.json")))!;
        Assert.That(Trades(one), Is.EqualTo(recorded.One));
        Assert.That(Trades(six), Is.EqualTo(recorded.Six));
    }

    [Test]
    public async Task A_set_over_three_pairs_has_a_difference_and_a_backtest_of_one_pair_has_none()
    {
        MoveApart();

        BacktestOutcome three = await Run(Generate());
        BacktestOutcome one = await Run(Generate(pairs: ["BTCUSDT"]));

        RandomDifference difference = three.Sets.Single().Numbers.Difference!;
        Assert.That(difference.Weeks, Is.EqualTo(1), "only the second week has a control group");
        Assert.That(three.Sets.Single().Pairs.Select(pair => pair.Numbers.Difference), Has.All.Not.Null);
        Assert.That(one.Sets.Single().Numbers.Trades, Is.GreaterThan(0));
        Assert.That(one.Sets.Single().Numbers.Difference, Is.Null);
    }

    /// <summary>
    ///     «Шесть наборов на одной истории», «Неделя со случайным входом», «Неделя без событий»: ETHUSDT has no orders; it
    ///     reads the minutes of the second week, where it is in the control group and the other pairs have orders, and not
    ///     those of the first, whose group is empty.
    /// </summary>
    [Test]
    public async Task Each_week_of_a_pair_reads_its_minutes_once_and_a_week_without_events_none()
    {
        _server.Data.HighPayments["eth"] = 3;
        _server.Data.HighPayments["sol"] = 0;

        await Run(Generate(Six));

        foreach (string pair in new[] { "btc", "eth", "sol" })
        {
            CandleHistoryRequest[] reads = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == pair)];
            Assert.That(reads.Count(request => request.Interval == CandleInterval.FourHours), Is.EqualTo(2), $"the 4h candles of {pair}");
            Assert.That(reads.Where(request => request.Interval != CandleInterval.FourHours).Select(request => request.From), Is.Unique, $"the minutes of {pair}");
            Assert.That(_server.Data.FundingRequests.Count(request => request.InstrumentIds.Single() == pair), Is.EqualTo(2), $"the funding of {pair}");
        }

        Assert.That(
            _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "eth" && request.Interval != CandleInterval.FourHours).Select(request => request.From.ToDateTimeOffset()),
            Is.EqualTo(new[] { SecondWeek }));
    }

    [Test]
    public async Task Two_same_backtests_have_the_same_hash_of_data_and_numbers()
    {
        MoveApart();

        BacktestOutcome first = await Run(Generate(Six));
        BacktestOutcome second = await Run(Generate(Six));

        Assert.That(second.DataHash, Is.EqualTo(first.DataHash));
        Assert.That(Numbers(second), Is.EqualTo(Numbers(first)));
    }

    /// <summary>
    ///     Decision 6: what a backtest reads a second time does not go into its hash.
    /// </summary>
    [Test]
    public async Task A_second_read_of_the_candles_and_the_funding_does_not_go_into_the_hash()
    {
        using var hash = new HistoryHash();
        using var empty = new HistoryHash();
        var history = new MarketHistory(_services.GetRequiredService<IHistorySource>(), End, hash, _server.Data.Time);

        await history.Again().Candles("btc", TimeSpan.FromHours(4), HistoryPeriod.DataStart, End, CancellationToken.None);
        await history.Again().FundingThrough("btc", HistoryPeriod.DataStart, End, CancellationToken.None);

        Assert.That(hash.Result(), Is.EqualTo(empty.Result()));
        await history.Candles("btc", TimeSpan.FromHours(4), HistoryPeriod.DataStart, End, CancellationToken.None);
        Assert.That(hash.Result(), Is.Not.EqualTo(empty.Result()));
    }

    /// <summary>
    ///     «Повторяемость прогона» (task 4.4): the set of 0.0005 on 4h alone and in a grid of six gives the same numbers, its
    ///     difference and t included. The prices of the fake do not move, so every week has the same difference and the t,
    ///     which needs a spread, is none in both.
    /// </summary>
    [Test]
    public async Task A_set_alone_and_among_six_gives_the_same_numbers_and_difference()
    {
        MoveApart();

        BacktestOutcome alone = await Run(Generate(["0.0005"], end: End.AddDays(14)));
        BacktestOutcome among = await Run(Generate(Six, end: End.AddDays(14)));

        (int _, SetSummary single, IReadOnlyList<PairSummary> singlePairs) = alone.Sets.Single();
        (int _, SetSummary third, IReadOnlyList<PairSummary> thirdPairs) = among.Sets[2];
        Assert.That(single.Difference, Is.Not.Null);
        Assert.That(single.Difference!.Weeks, Is.EqualTo(3), "the weeks from 2026-01-12 to 2026-02-02");
        Assert.That(JsonSerializer.Serialize(third), Is.EqualTo(JsonSerializer.Serialize(single)));
        Assert.That(JsonSerializer.Serialize(thirdPairs), Is.EqualTo(JsonSerializer.Serialize(singlePairs)));
    }

    // The high payment of ETHUSDT and of SOLUSDT at other times of the day than that of BTCUSDT.
    private void MoveApart()
    {
        _server.Data.HighPayments["eth"] = 2;
        _server.Data.HighPayments["sol"] = 0;
    }

    private static Backtest Generate(IReadOnlyList<string>? thresholds = null, IReadOnlyList<string>? pairs = null, DateTimeOffset? end = null)
    {
        Backtest backtest = BacktestGenerator.Generate(thresholds: thresholds);
        return new Backtest
        {
            Id = backtest.Id,
            StrategyId = backtest.StrategyId,
            StrategyName = backtest.StrategyName,
            StrategyVersion = backtest.StrategyVersion,
            Kind = backtest.Kind,
            Graph = backtest.Graph,
            Settings = backtest.Settings with { End = end ?? End, Pairs = pairs },
            Sets = backtest.Sets,
            SetCount = backtest.SetCount
        };
    }

    private async Task<BacktestOutcome> Run(Backtest backtest)
    {
        IBacktestRepository backtests = _services.GetRequiredService<IBacktestRepository>();
        await backtests.EnqueueAsync(backtest, CancellationToken.None);
        Backtest claimed = (await backtests.ClaimAsync("w-1", Lease, CancellationToken.None))!;
        ExecuteBacktestResult result = await _services.GetRequiredService<ISender>().Send(new ExecuteBacktestCommand(claimed, new RunAssignment(claimed.Id, "w-1")));
        return result.Outcome;
    }

    // The numbers of the sets and of their pairs as text: equal text, equal numbers.
    private static string Numbers(BacktestOutcome outcome)
    {
        return JsonSerializer.Serialize(outcome.Sets.Select(set => new { set.Set, set.Numbers, set.Pairs }));
    }

    // The numbers of the sets and of their pairs but the difference, as text; the numbers by weeks, new in engine version 5,
    // are left out as the text of version 4 has none.
    private static string[] Trades(BacktestOutcome outcome)
    {
        return
        [
            .. outcome.Sets.Select(set =>
                $"{set.Set}: {Text(set.Numbers)} [{string.Join(',', set.Pairs.Select(pair => $"{{\"Pair\":{JsonSerializer.Serialize(pair.Pair)},\"Numbers\":{Text(pair.Numbers)}}}"))}]")
        ];
    }

    private static string Text(SetSummary numbers)
    {
        return JsonSerializer.Serialize(numbers with { Difference = null, Weekly = null }).Replace(",\"Weekly\":null", "", StringComparison.Ordinal);
    }

    // What engine version 4 gave, set by set, for one set and for six, as Trades gives it.
    private sealed record Version4Numbers(
        [property: JsonPropertyName("one")] string[] One,
        [property: JsonPropertyName("six")] string[] Six);
}