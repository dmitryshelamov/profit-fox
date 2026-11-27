using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.Services.Strategies;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;
using ProfitFox.Tests.Unit.Domain.Services.Runs;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies;

/// <summary>
///     Change add-strategy-graph (task 4.2): the steps of a strategy on one pair as a run of the bench sees them — the
///     orders at the events of the signal, and the values of the exit nodes at any moment a cycle is opened outside the
///     graph.
/// </summary>
public sealed class PairStepsTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly Signal Signal = StrategyGraph.ReadSignal(Graphs.H0001).Value!;
    private static readonly TradingStrategy H0001 = Designs.H0001;

    [Test]
    public void The_orders_come_at_the_events_of_the_signal_and_a_candle_without_trades_is_not_given_to_the_strategy()
    {
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Untraded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8)), Traded(Utc(2026, 1, 5, 12))];
        HistoryPayment[] funding = [new HistoryPayment(Utc(2026, 1, 5, 0), "0.0001"), new HistoryPayment(Utc(2026, 1, 5, 6), "0.001")];

        var steps = PairSteps.Of(H0001, FourHours, "BTCUSDT", candles, funding);

        Assert.That(steps.Orders, Is.EqualTo(new[] { new StrategyStep(Utc(2026, 1, 5, 12), EntrySide.Short, FourHours, ExitDistance.None, ExitDistance.None) { Node = "enter" } }));
        Assert.That(steps.Orders.Select(order => order.Moment), Is.EqualTo(PairEvents.Of(Signal, FourHours, "BTCUSDT", candles, funding)));
    }

    [Test]
    public void A_moment_gets_the_values_of_the_last_candle_closed_by_it_and_no_order()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Graphs.Raised(StopFromTheClose)).Value!;
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0), "0.01"), Untraded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8), "0.03")];

        var steps = PairSteps.Of(strategy, FourHours, "BTCUSDT", candles, []);

        Assert.That(steps.Orders.Select(order => order.Moment), Is.EqualTo(new[] { Utc(2026, 1, 5, 12) }));
        Assert.That(steps.At(Utc(2026, 1, 5, 4)), Is.EqualTo(new StrategyStep(Utc(2026, 1, 5, 4), Entry: null, FourHours, ExitDistance.Of(0.01m), ExitDistance.None)));
        Assert.That(steps.At(Utc(2026, 1, 5, 8)).Stop, Is.EqualTo(ExitDistance.Of(0.01m)), "the candle closed at 08:00 had no trades: the strategy was not computed");
        Assert.That(steps.At(Utc(2026, 1, 5, 12)), Is.EqualTo(steps.Orders[0] with { Entry = null, Node = null }), "a cycle outside the graph is not the order of the candle");
        Assert.That(steps.At(Utc(2026, 1, 5, 16)), Is.EqualTo(new StrategyStep(Utc(2026, 1, 5, 16), Entry: null, FourHours, ExitDistance.Of(0.03m), ExitDistance.None)));
    }

    [Test]
    public void Before_the_first_candle_only_the_written_values_are_known()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Graphs.Raised(StopFromTheClose)).Value!;

        var steps = PairSteps.Of(strategy, FourHours, "BTCUSDT", [Traded(Utc(2026, 1, 5, 4), "0.01")], []);

        Assert.That(steps.At(Utc(2026, 1, 5, 4)), Is.EqualTo(new StrategyStep(Utc(2026, 1, 5, 4), Entry: null, FourHours, ExitDistance.Of(null), ExitDistance.None)));
        Assert.That(PairSteps.Of(H0001, FourHours, "BTCUSDT", [], []).At(Utc(2026, 1, 5, 0)).After, Is.EqualTo(FourHours));
    }

    // Enters long when the close first goes above 0.02; the stop is the close of the candle.
    private const string StopFromTheClose =
        """
        {
          "formatVersion": 1,
          "nodes": [
            { "id": "c", "kind": "candle" },
            { "id": "above", "kind": "compare", "settings": { "operation": "gt" }, "inputs": { "b": 0.02 } },
            { "id": "first", "kind": "first-time" },
            { "id": "enter", "kind": "enter", "settings": { "side": "long" } },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } },
            { "id": "stop-loss", "kind": "stop-loss" }
          ],
          "wires": [
            { "from": "c.close", "to": "above.a" },
            { "from": "above.result", "to": "first.value" },
            { "from": "first.event", "to": "enter.when" },
            { "from": "c.close", "to": "stop-loss.distance" }
          ]
        }
        """;

    /// <summary>
    ///     Change add-ideas-and-market, task 4.2: a strategy with <c>pair-candle</c> gives an order by the candle of the
    ///     other pair.
    /// </summary>
    [Test]
    public void A_strategy_with_a_pair_candle_gives_an_order_by_the_candle_of_the_other_pair()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(BtcBelow, StrategyKind.Idea).Value!;
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Traded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8))];
        HistoryCandle[] btc = [Traded(Utc(2026, 1, 5, 0), "96000"), Traded(Utc(2026, 1, 5, 4), "94000"), Untraded(Utc(2026, 1, 5, 8))];

        var steps = PairSteps.Of(strategy, FourHours, "SOLUSDT", candles, [], new Dictionary<string, IReadOnlyList<HistoryCandle>> { ["BTCUSDT"] = btc });

        Assert.That(StrategyGraph.NamedPairs(strategy.Graph), Is.EqualTo(new[] { "BTCUSDT" }));
        Assert.That(steps.Orders.Select(order => order.Moment), Is.EqualTo(new[] { Utc(2026, 1, 5, 8) }), "BTC closed below 95000 in the candle of 04:00");
        Assert.That(PairSteps.Of(strategy, FourHours, "SOLUSDT", candles, []).Orders, Is.Empty, "without the candles of BTC the node gives nothing");
    }

    /// <summary>
    ///     Change add-ideas-and-market, task 4.3: spec market-history «Дыра у названной пары» — a candle of the named pair
    ///     outside its coverage is not read, and the node gives missing values on it.
    /// </summary>
    [Test]
    public void A_hole_in_the_history_of_the_named_pair_gives_missing_values()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(BtcBelow, StrategyKind.Idea).Value!;
        HistoryCandle[] candles = [Traded(Utc(2026, 1, 5, 0)), Traded(Utc(2026, 1, 5, 4)), Traded(Utc(2026, 1, 5, 8)), Traded(Utc(2026, 1, 5, 12))];
        HistoryCandle[] btc = [Traded(Utc(2026, 1, 5, 0), "96000"), Traded(Utc(2026, 1, 5, 4), "94000"), Traded(Utc(2026, 1, 5, 8), "96000"), Traded(Utc(2026, 1, 5, 12), "94000")];
        HistoryCandle[] holed = [btc[0], btc[2], btc[3]];

        var whole = PairSteps.Of(strategy, FourHours, "SOLUSDT", candles, [], new Dictionary<string, IReadOnlyList<HistoryCandle>> { ["BTCUSDT"] = btc });
        var steps = PairSteps.Of(strategy, FourHours, "SOLUSDT", candles, [], new Dictionary<string, IReadOnlyList<HistoryCandle>> { ["BTCUSDT"] = holed });

        Assert.That(whole.Orders.Select(order => order.Moment), Is.EqualTo(new[] { Utc(2026, 1, 5, 8), Utc(2026, 1, 5, 16) }));
        Assert.That(steps.Orders.Select(order => order.Moment), Is.EqualTo(new[] { Utc(2026, 1, 5, 16) }), "the candle of 04:00 is a hole: its close is missing, and the drop on it is not seen");
    }

    // A short when the close of BTCUSDT is first below 95000, closed after 4h.
    private const string BtcBelow =
        """
        {
          "formatVersion": 1,
          "nodes": [
            { "id": "btc", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } },
            { "id": "low", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": 95000 } },
            { "id": "first", "kind": "first-time" },
            { "id": "enter", "kind": "enter", "settings": { "side": "short" }, "inputs": { "size": 100 } },
            { "id": "position", "kind": "position" },
            { "id": "close-after", "kind": "close-after", "inputs": { "after": "4h" } }
          ],
          "wires": [
            { "from": "btc.close", "to": "low.a" },
            { "from": "low.result", "to": "first.value" },
            { "from": "first.event", "to": "enter.when" },
            { "from": "enter.order", "to": "position.orders" },
            { "from": "position.position", "to": "close-after.position" },
            { "from": "close-after.order", "to": "position.orders" }
          ]
        }
        """;

    private static HistoryCandle Traded(DateTimeOffset open, string close = "100.5")
    {
        return new HistoryCandle(open, close, close, close, close, "12.5", Turnover: null, BuyVolume: null);
    }

    private static HistoryCandle Untraded(DateTimeOffset open)
    {
        return new HistoryCandle(open, "100", "100", "100", "100", "0", Turnover: null, BuyVolume: null);
    }
}
