using System.Globalization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Hypotheses.AddHypothesis;
using ProfitFox.Application.Commands.Runs.ExecuteRun;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Application.Commands.Runs;

/// <summary>
///     Change add-position-node, task 4.2: on the reference history of <see cref="EngineVersionTests" /> the cycle of every
///     event and every random entry of H0001 and H0002 is the same by <see cref="CycleRun" /> and by
///     <see cref="TradeSimulator" />, every field to the last bit, on the minute path of its block as the bench builds it;
///     and an outcome the bench counted is what <see cref="CycleRun" /> gives on that path. The bench itself still counts
///     with <see cref="TradeSimulator" /> here.
/// </summary>
[NonParallelizable]
public sealed class CycleRunParityTests
{
    private static readonly DateTimeOffset Today = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    [SetUp]
    public async Task CreateBench()
    {
        FakeMarketData.CoveredUntil = FakeMarketData.CoveredFrom.AddDays(28);
        await BenchDatabase.Reset();
        foreach (string reference in new[] { "H0001", "H0002" })
        {
            await BenchServices.Sender.Send(new AddHypothesisCommand(File.ReadAllText(Path.Combine(Fixtures, $"{reference}.json"))), CancellationToken.None);
        }
    }

    [TearDown]
    public void RemoveBench()
    {
        FakeMarketData.CoveredUntil = null;
    }

    [TestCase("H0001", 4)]
    [TestCase("H0002", 48)]
    public async Task Every_cycle_of_the_reference_is_the_same_by_both_calculations(string hypothesis, int hours)
    {
        await using MarketDataServer server = await MarketDataServer.Start();
        await using ServiceProvider services = BenchServices.Start(server);
        ExecuteRunResult result = await services.GetRequiredService<ISender>().Send(
            new ExecuteRunCommand(hypothesis, Version: null, Period.Exploration, Parity: false, Today),
            CancellationToken.None);
        Hypothesis reference = HypothesisReader.Parse(File.ReadAllText(Path.Combine(Fixtures, $"{hypothesis}.json")));
        var design = new RunDesign(reference, Period.Exploration, Periods.DataStart, Periods.ExplorationEnd);
        TradePlan plan = reference.Plan;
        Assert.That(plan, Is.EqualTo(new TradePlan(TradeSide.Short, TimeSpan.FromHours(hours))));
        Dictionary<(string, long), MinutePath> paths = await Paths(services, design, result.Rows);

        int compared = 0;
        int counted = 0;
        foreach (EventRow row in result.Rows)
        {
            MinutePath path = paths[(row.Instrument, row.Block)];
            IEnumerable<(DateTimeOffset Moment, TradeOutcome? Bench)> cycles = row.Randoms.Select(random => (random.Entry, (TradeOutcome?)random.Outcome)).Prepend((row.Moment, row.Outcome));
            foreach ((DateTimeOffset moment, TradeOutcome? bench) in cycles)
            {
                TradeOutcome simulated = TradeSimulator.Simulate(path, moment, plan, reference.Costs);
                TradeOutcome cycle = CycleRun.Simulate(path, moment, plan, reference.Costs, amount: 100).ToTrade();
                Assert.That(Text(cycle), Is.EqualTo(Text(simulated)), $"{row.Instrument} {moment:O}");
                Assert.That(CycleRun.Exit(path, moment, plan, reference.Costs), Is.EqualTo(TradeSimulator.Exit(path, moment, plan, reference.Costs)));
                compared++;
                if (bench is { Status: TradeStatus.Ok })
                {
                    Assert.That(Text(cycle), Is.EqualTo(Text(bench)), $"the bench counted {row.Instrument} {moment:O} so");
                    counted++;
                }
            }
        }

        TestContext.Out.WriteLine($"{hypothesis}: {compared} cycles compared, {counted} of them as the bench counted them");
        Assert.That(compared, Is.GreaterThan(100));
        Assert.That(counted, Is.GreaterThan(100));
    }

    // The minute path of the block of each row, built as the bench builds it (ExecuteRunCommandHandler).
    private static async Task<Dictionary<(string, long), MinutePath>> Paths(ServiceProvider services, RunDesign design, IEnumerable<EventRow> rows)
    {
        IReadOnlyList<Instrument> pairs = await services.GetRequiredService<IInstrumentCatalog>().LinearUsdtPerpetuals(CancellationToken.None);
        using var hash = new HistoryHash();
        var history = new MarketHistory(services.GetRequiredService<IHistorySource>(), design.PeriodEnd, hash);
        var paths = new Dictionary<(string, long), MinutePath>();
        foreach ((string symbol, long block) in rows.Select(row => (row.Instrument, row.Block)).Distinct())
        {
            string id = pairs.Single(pair => pair.Symbol == symbol).Id;
            (DateTimeOffset from, DateTimeOffset to) = design.Span(block);
            HistoryRead<HistoryCandle> minutes = await history.Candles(id, TimeSpan.FromMinutes(1), from, to, CancellationToken.None);
            HistoryRead<HistoryPayment> funding = await history.FundingThrough(id, Periods.DataStart, design.PeriodEnd, CancellationToken.None);
            paths[(symbol, block)] = MinutePaths.Build(from, to, minutes, funding);
        }

        return paths;
    }

    private static string Text(TradeOutcome outcome)
    {
        static string Bits(double? value)
        {
            return value is { } number ? BitConverter.DoubleToInt64Bits(number).ToString("x16", CultureInfo.InvariantCulture) : "-";
        }

        return string.Join(
            ";",
            outcome.Status,
            outcome.ExitReason,
            outcome.Ambiguous,
            outcome.EntryTime?.ToUnixTimeMilliseconds(),
            outcome.ExitTime?.ToUnixTimeMilliseconds(),
            Bits(outcome.EntryPrice),
            Bits(outcome.ExitPrice),
            Bits(outcome.Gross),
            Bits(outcome.Fees),
            Bits(outcome.Slippage),
            Bits(outcome.Funding),
            Bits(outcome.Net),
            Bits(outcome.R));
    }
}
