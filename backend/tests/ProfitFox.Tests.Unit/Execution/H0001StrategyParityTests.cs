using System.Globalization;
using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;
using ProfitFox.Tests.Unit.Graph;

namespace ProfitFox.Tests.Unit.Execution;

/// <summary>
///     Spec strategy-execution «Стратегия выдаёт те же события, что её сигнал» (change add-strategy-graph, task 2.2): on
///     the history of <see cref="H0001ParityTests" /> the orders of the strategy of H0001 come at the moments of the events
///     of its signal, and the pairs of the history, which come mixed, do not reach each other.
/// </summary>
public sealed class H0001StrategyParityTests
{
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Graph", "Fixtures", "H0001");

    private static readonly Signal Signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;
    private static readonly TradingStrategy Strategy = TradingStrategy.FromSignal(Signal, EntrySide.Short, TimeSpan.FromHours(4));

    [Test]
    public void The_orders_of_the_strategy_come_at_the_events_of_the_signal()
    {
        List<(string Instrument, ClosedCandle Candle, FundingPayment? Funding)> history = History();
        var signal = new SignalExecutor(Signal);
        var strategy = new StrategyExecutor(Strategy);
        List<(string, DateTimeOffset)> events = [];
        List<(string, DateTimeOffset)> orders = [];

        foreach ((string instrument, ClosedCandle candle, FundingPayment? funding) in history)
        {
            if (signal.Step(instrument, candle, funding) is { } found)
            {
                events.Add((found.Instrument, found.Moment));
            }

            StrategyStep step = strategy.Step(instrument, candle, funding);
            Assert.That(step.Moment, Is.EqualTo(candle.CloseTime));
            Assert.That(step.After, Is.EqualTo(TimeSpan.FromHours(4)));
            if (step.Entry is { } side)
            {
                Assert.That(side, Is.EqualTo(EntrySide.Short));
                orders.Add((instrument, step.Moment));
            }
        }

        Assert.That(events, Has.Count.GreaterThan(100));
        Assert.That(orders, Is.EqualTo(events));
        Assert.That(orders, Is.EquivalentTo(Rows("events.csv").Select(row => (row[0], Time(row[1])))), "and these are the events of event.py");
    }

    [Test]
    public void The_pairs_of_the_history_do_not_reach_each_other()
    {
        List<(string Instrument, ClosedCandle Candle, FundingPayment? Funding)> history = History();
        var together = new StrategyExecutor(Strategy);
        List<(string Instrument, StrategyStep Step)> mixed = [.. history.Select(item => (item.Instrument, together.Step(item.Instrument, item.Candle, item.Funding)))];

        string[] instruments = [.. history.Select(item => item.Instrument).Distinct(StringComparer.Ordinal)];
        Assert.That(instruments, Has.Length.GreaterThan(1), "the history has several pairs");
        foreach (string instrument in instruments)
        {
            var alone = new StrategyExecutor(Strategy);
            StrategyStep[] own = [.. history.Where(item => item.Instrument == instrument).Select(item => alone.Step(instrument, item.Candle, item.Funding))];

            Assert.That(mixed.Where(item => item.Instrument == instrument).Select(item => item.Step), Is.EqualTo(own), instrument);
        }
    }

    // The bars of the fixture in its order, each with the last payment of its pair paid by its close.
    private static List<(string Instrument, ClosedCandle Candle, FundingPayment? Funding)> History()
    {
        Dictionary<string, List<FundingPayment>> payments = Rows("funding.csv")
            .GroupBy(row => row[0], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new FundingPayment(Time(row[1]), decimal.Parse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture)))
                    .OrderBy(payment => payment.Time)
                    .ToList(),
                StringComparer.Ordinal);
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        List<(string, ClosedCandle, FundingPayment?)> history = [];
        foreach (string[] row in Rows("bars.csv"))
        {
            string instrument = row[0];
            var candle = new ClosedCandle(Time(row[1]), Time(row[2]), 0, 0, 0, 0, 0);
            List<FundingPayment> paid = payments.GetValueOrDefault(instrument) ?? [];
            int index = next.GetValueOrDefault(instrument);
            while (index < paid.Count && paid[index].Time <= candle.CloseTime)
            {
                index++;
            }

            next[instrument] = index;
            history.Add((instrument, candle, index > 0 ? paid[index - 1] : null));
        }

        return history;
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Path.Combine(Folder, file)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
