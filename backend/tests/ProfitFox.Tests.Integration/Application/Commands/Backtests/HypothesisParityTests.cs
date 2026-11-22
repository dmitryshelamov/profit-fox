using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Commands.Hypotheses.AddHypothesis;
using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     Change add-strategy-backtest, task 5.1 (design.md, decision 7, step 1): H0001 and H0002 on the reference history of
///     engine version 2 — the synthetic history of the fake market-data service, exploration up to 2026-05-16 — by the run
///     of a hypothesis and by <see cref="PairBacktest" /> with the strategy of the hypothesis, its hash as the seed and its
///     block and random entries. The orders with their trades, the random entries with their trades and the weekly means
///     are equal exactly. The test goes with the run of hypotheses (task 8.3).
/// </summary>
[NonParallelizable]
public sealed class HypothesisParityTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [TestCase("H0001")]
    [TestCase("H0002")]
    public async Task The_backtest_of_the_strategy_of_a_hypothesis_gives_the_rows_and_weekly_means_of_its_run(string reference)
    {
        string text = File.ReadAllText(Path.Combine(Fixtures, $"{reference}.json"));
        await BenchServices.Sender.Send(new AddHypothesisCommand(text), CancellationToken.None);
        Hypothesis hypothesis = HypothesisReader.Parse(text);
        await using MarketDataServer server = await MarketDataServer.Start();
        await using ServiceProvider services = BenchServices.Start(server);

        ExecuteRunResult run = await services.GetRequiredService<ISender>().Send(
            new ExecuteRunCommand(reference, Version: null, Period.Exploration, Parity: false, Today),
            CancellationToken.None);
        (List<EventRow> rows, SetNumbers numbers) = await Backtest(services, hypothesis);

        Assert.That(rows, Has.Count.EqualTo(run.Rows.Count));
        for (int index = 0; index < rows.Count; index++)
        {
            EventRow expected = run.Rows[index];
            EventRow actual = rows[index];
            Assert.That(
                (actual.Instrument, actual.Moment, actual.Block, actual.Status, actual.Outcome, actual.Event, actual.RandomValid, actual.RandomMean, actual.PseudoDifference),
                Is.EqualTo((expected.Instrument, expected.Moment, expected.Block, expected.Status, expected.Outcome, expected.Event, expected.RandomValid, expected.RandomMean, expected.PseudoDifference)),
                $"row {index}");
            Assert.That(actual.Randoms.Select(random => (random.Entry, random.Outcome)), Is.EqualTo(expected.Randoms.Select(random => (random.Entry, random.Outcome))), $"random entries of row {index}");
        }

        Assert.That(numbers.WeekMeans(), Is.EqualTo(Evidence.BlockMeans(run.Rows)));
        Assert.That(numbers.Skipped, Is.EqualTo(run.Skipped));
        TestContext.Out.WriteLine(
            $"{reference}: {rows.Count} orders, {rows.Count(row => row.Status == ProfitFox.Domain.ValueObjects.Trades.Enums.TradeStatus.Ok)} trades, "
            + $"{rows.Sum(row => row.Randoms.Count)} random entries, {numbers.WeekMeans().Count} weekly means, {numbers.Skipped} skipped");
    }

    // The strategy of the hypothesis as a set of a backtest on every pair, the pairs in the order of the catalog.
    private static async Task<(List<EventRow> Rows, SetNumbers Numbers)> Backtest(ServiceProvider services, Hypothesis hypothesis)
    {
        var design = new BacktestDesign(HistoryPeriod.DataStart, End, hypothesis.Timeframe, hypothesis.Hash, hypothesis.Costs)
        {
            Block = hypothesis.Block,
            Randoms = hypothesis.RandomsPerEvent
        };
        BacktestPlan[] plans = [new BacktestPlan(1, hypothesis.Strategy, design)];
        using var hash = new HistoryHash();
        var history = new MarketHistory(services.GetRequiredService<IHistorySource>(), End, hash);
        List<EventRow> rows = [];
        SetNumbers numbers = SetNumbers.Zero;
        foreach (Instrument pair in await services.GetRequiredService<IInstrumentCatalog>().LinearUsdtPerpetuals(CancellationToken.None))
        {
            PairSetResult result = (await PairBacktestRun.RunAsync(history, pair, StrategyKind.Idea, plans, CancellationToken.None)).Single();
            rows.AddRange(result.Rows);
            numbers += result.Numbers;
        }

        return (rows, numbers);
    }
}
