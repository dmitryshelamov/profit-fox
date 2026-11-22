using Grpc.Core;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
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

    private static Backtest Generate(IReadOnlyList<string>? thresholds = null)
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
            Graph = backtest.Graph,
            Settings = settings,
            Sets = backtest.Sets,
            SetCount = backtest.SetCount
        };
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
