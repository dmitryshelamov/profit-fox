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
///     «Источник истории», «Граница периода», «Чтение без копии», «Минутные свечи в окнах сделок» («Шесть наборов на одной
///     истории») and spec strategy-backtest «История прогона» («История после конца»).
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

    [Test]
    public async Task Six_sets_on_one_timeframe_read_the_candles_and_the_minutes_of_each_week_of_a_pair_once()
    {
        await Run(Generate(thresholds: ["0.0003", "0.0004", "0.0005", "0.00055", "0.0006", "0.001"]));

        CandleHistoryRequest[] btc = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "btc")];
        Assert.That(btc.Count(request => request.Interval == CandleInterval.FourHours), Is.EqualTo(1));
        CandleHistoryRequest[] minutes = [.. btc.Where(request => request.Interval != CandleInterval.FourHours)];
        Assert.That(minutes.Select(request => request.From), Is.Unique, "each week once for the six sets");
        Assert.That(minutes, Has.Length.EqualTo(2), "two weeks of history have orders");
        Assert.That(_server.Data.FundingRequests.Count(request => request.InstrumentIds.Single() == "btc"), Is.EqualTo(1));
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
    ///     Task 12.1: the market-data service holds past days only. With the end of history at the start of the current day the
    ///     funding is read up to it and not past it; a trade that ends exactly there is left out on funding, because the
    ///     payment at its end is not known yet, and every trade that ends before it is as with a later clock.
    /// </summary>
    [Test]
    public async Task A_backtest_up_to_today_reads_no_funding_of_today_and_leaves_the_trade_that_ends_there_out_on_funding()
    {
        // The strategy enters at 08:00 every day for 16 hours: its trade of the last day ends exactly at the end of history.
        Backtest later = await Execute(Generate(after: "16h"), new FixedTime(End.AddDays(1).AddHours(9)));
        _server.Data.FundingRequests.Clear();

        Backtest today = await Execute(Generate(after: "16h"), new FixedTime(End.AddHours(9)));

        Assert.That(today.Status, Is.EqualTo(BacktestState.Done), today.Reason);
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(End));
        SetSummary before = later.Sets.Single().Numbers!;
        SetSummary now = today.Sets.Single().Numbers!;
        Assert.That(before.Excluded.Funding, Is.Zero, "with a later clock the payment at the end is known");
        Assert.That(now.Excluded.Funding, Is.EqualTo(3), "the trade of the last day of each of the three pairs");
        Assert.That(now.Trades, Is.EqualTo(before.Trades - 3));
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
