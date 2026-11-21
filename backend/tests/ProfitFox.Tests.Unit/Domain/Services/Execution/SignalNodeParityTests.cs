using System.Globalization;
using System.Text.Json.Nodes;

using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Execution;

/// <summary>
///     Spec strategy-graph «Раскрытая стратегия считает так же» and «Несколько узлов сигнала» (change
///     add-strategy-constructor, task 2.4): on the history of <see cref="H0001ParityTests" /> a strategy that holds the
///     signal of H0001 as a node gives the steps of the strategy of the hypothesis H0001 at every candle of every pair.
/// </summary>
public sealed class SignalNodeParityTests
{
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Domain", "Services", "Execution", "Fixtures", "H0001");

    [Test]
    public void H0001_through_a_signal_node()
    {
        var hypothesis = TradingStrategy.FromSignal(StrategyGraph.ReadSignal(Graphs.H0001).Value!, EntrySide.Short, TimeSpan.FromHours(4));
        TradingStrategy throughNode = StrategyGraph.ReadStrategy(SignalLibrary.Strategy(), SignalLibrary.Set).Value!;
        var expected = new StrategyExecutor(hypothesis);
        var actual = new StrategyExecutor(throughNode);
        int orders = 0;

        foreach ((string instrument, ClosedCandle candle, FundingPayment? funding) in History())
        {
            StrategyStep step = actual.Step(instrument, candle, funding);
            Assert.That(step, Is.EqualTo(expected.Step(instrument, candle, funding)), $"{instrument} {candle.CloseTime:O}");
            orders += step.Entry is null ? 0 : 1;
        }

        Assert.That(orders, Is.GreaterThan(100));
        Assert.That(actual.BeforeCandles(DateTimeOffset.UnixEpoch), Is.EqualTo(expected.BeforeCandles(DateTimeOffset.UnixEpoch)));
    }

    [Test]
    public void One_signal_with_two_thresholds()
    {
        // low enters long at 0.0005, high enters short at 0.001: the side of an order tells which node gave it.
        string json = $$"""
                        {
                          "formatVersion": 1,
                          "nodes": [
                            { "id": "low", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "1" }, "inputs": { "threshold": 0.0005 } },
                            { "id": "high", "kind": "signal", "settings": { "signal": "{{SignalLibrary.FundingHigh}}", "version": "1" }, "inputs": { "threshold": 0.001 } },
                            { "id": "long", "kind": "enter", "settings": { "side": "long" } },
                            { "id": "short", "kind": "enter", "settings": { "side": "short" } },
                            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
                          ],
                          "wires": [{ "from": "low.event", "to": "long.when" }, { "from": "high.event", "to": "short.when" }]
                        }
                        """;
        ReadResult<TradingStrategy> result = StrategyGraph.ReadStrategy(json, SignalLibrary.Set);
        Assert.That(result.Errors, Is.Empty);
        var strategy = new StrategyExecutor(result.Value!);
        var low = new SignalExecutor(Signal(0.0005m));
        var high = new SignalExecutor(Signal(0.001m));
        List<(string, DateTimeOffset, EntrySide)> orders = [];
        List<(string, DateTimeOffset, EntrySide)> events = [];

        foreach ((string instrument, ClosedCandle candle, FundingPayment? funding) in History())
        {
            if (strategy.Step(instrument, candle, funding).Entry is { } side)
            {
                orders.Add((instrument, candle.CloseTime, side));
            }

            bool lowEvent = low.Step(instrument, candle, funding) is not null;
            bool highEvent = high.Step(instrument, candle, funding) is not null;
            // Entries of both sides at one candle give no order (spec strategy-position).
            if (lowEvent != highEvent)
            {
                events.Add((instrument, candle.CloseTime, lowEvent ? EntrySide.Long : EntrySide.Short));
            }
        }

        Assert.That(events.Count(item => item.Item3 == EntrySide.Short), Is.GreaterThan(0), "the history reaches 0.001 without reaching 0.0005 first somewhere");
        Assert.That(orders, Is.EqualTo(events));
    }

    // The signal of H0001 with its threshold written.
    private static Signal Signal(decimal threshold)
    {
        string json = Graphs.Changed(graph => graph["parameters"]![0]!["value"] = JsonValue.Create(threshold));
        return StrategyGraph.ReadSignal(json).Value!;
    }

    // The bars of the fixture in its order, each with the last payment of its pair paid by its close.
    private static List<(string Instrument, ClosedCandle Candle, FundingPayment? Funding)> History()
    {
        var payments = Rows("funding.csv")
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
