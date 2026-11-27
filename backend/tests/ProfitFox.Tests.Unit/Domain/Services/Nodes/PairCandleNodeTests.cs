using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Nodes;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Nodes;
using ProfitFox.Tests.Unit.Domain.Services.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-ideas-and-market, task 4.1: spec strategy-execution «Свеча другой пары» — the node <c>pair-candle</c>
///     gives the closed candle of the named pair with the open time of the candle being computed.
/// </summary>
public sealed class PairCandleNodeTests
{
    private const string Btc = """{ "id": "btc", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } }""";

    [Test]
    public void The_node_gives_the_close_of_the_named_pair()
    {
        var run = new GraphRun(History.Graph(History.WithOutput("number", "btc.close", Btc)));
        ClosedCandle sol = Candle(open: 2, close: 238.42m);

        PinValue close = run.Step(sol, facts: null, pairs: Pairs(("BTCUSDT", Candle(open: 2, close: 96000m))))[0];

        Assert.That(sol.OpenTime, Is.EqualTo(History.Start.AddHours(8)));
        Assert.That(close.Number, Is.EqualTo(96000m));
    }

    [Test]
    public void Without_the_candle_of_the_named_pair_the_outputs_are_missing_and_a_comparison_is_not_true()
    {
        string json = History.WithOutput(
            "boolean",
            "low.result",
            $$"""{{Btc}}, { "id": "low", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": 100000 } }""",
            """{ "from": "btc.close", "to": "low.a" }""");
        var run = new GraphRun(History.Graph(json));

        PinValue other = run.Step(Candle(open: 0, close: 1), facts: null, pairs: Pairs(("ETHUSDT", Candle(open: 0, close: 3000))))[0];
        PinValue shifted = run.Step(Candle(open: 1, close: 1), facts: null, pairs: Pairs(("BTCUSDT", Candle(open: 0, close: 96000))))[0];
        PinValue none = run.Step(Candle(open: 2, close: 1), facts: null)[0];

        // The released compare gives a missing value on a missing input (spec strategy-execution «Отсутствующие значения»):
        // the comparison is not true, so «впервые» after it gives no event.
        Assert.That((other.Boolean, shifted.Boolean, none.Boolean), Is.EqualTo(((bool?)null, (bool?)null, (bool?)null)));
        Assert.That(new[] { other, shifted, none }.Any(value => value.Boolean == true), Is.False);
        var values = new GraphRun(History.Graph(History.WithOutput("number", "btc.open", Btc)));
        Assert.That(values.Step(Candle(open: 0, close: 1), facts: null)[0].IsMissing, Is.True);
    }

    [Test]
    public void An_empty_symbol_is_an_error_of_the_setting_of_the_node()
    {
        string empty = History.WithOutput("number", "btc.close", """{ "id": "btc", "kind": "pair-candle", "settings": { "symbol": "" } }""");
        string absent = History.WithOutput("number", "btc.close", """{ "id": "btc", "kind": "pair-candle" }""");

        GraphError emptyError = StrategyGraph.Read(empty).Errors.Single();
        GraphError absentError = StrategyGraph.Read(absent).Errors.Single();

        Assert.That((emptyError.Code, emptyError.Node, emptyError.Setting), Is.EqualTo((GraphErrorCode.InvalidSetting, (string?)"btc", (string?)"symbol")));
        Assert.That((absentError.Code, absentError.Node, absentError.Setting), Is.EqualTo((GraphErrorCode.MissingSetting, (string?)"btc", (string?)"symbol")));
    }

    [Test]
    public void On_the_named_pair_itself_the_node_gives_what_the_candle_gives()
    {
        string json = History.WithOutput("number", "btc.high", $$"""{{Btc}}, { "id": "c", "kind": "candle" }""");
        string candle = History.WithOutput("number", "c.high", """{ "id": "c", "kind": "candle" }""");
        var run = new GraphRun(History.Graph(json));
        var own = new GraphRun(History.Graph(candle));
        var btc = new ClosedCandle(History.Start, History.Start + History.Bar, 95000, 96500, 94800, 96000, 12);

        Assert.That(run.Step(btc, facts: null, pairs: Pairs(("BTCUSDT", btc)))[0], Is.EqualTo(own.Step(btc, facts: null)[0]));
    }

    [Test]
    public void The_kind_is_in_the_catalog_of_a_signal_with_the_outputs_of_a_candle_and_the_setting_symbol()
    {
        NodeKind kind = NodeCatalog.Find("pair-candle")!;

        Assert.That(kind.AllowedInSignal, Is.True);
        Assert.That(kind.Outputs(History.Graph(History.WithOutput("number", "btc.close", Btc))).Select(pin => pin.Name), Is.EqualTo(new[] { "open", "high", "low", "close", "volume" }));
        Assert.That(kind.Settings.Single().Name, Is.EqualTo("symbol"));
    }

    [Test]
    public void The_named_pairs_of_a_graph_and_of_an_expanded_strategy()
    {
        string json = History.WithOutput(
            "number",
            "eth.close",
            $$"""{{Btc}}, { "id": "eth", "kind": "pair-candle", "settings": { "symbol": "ETHUSDT" } }, { "id": "btc2", "kind": "pair-candle", "settings": { "symbol": "BTCUSDT" } }""");

        Assert.That(StrategyGraph.NamedPairs(History.Graph(json)), Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
        Assert.That(StrategyGraph.NamedPairs(History.Graph(History.WithOutput("number", "c.close", """{ "id": "c", "kind": "candle" }"""))), Is.Empty);
    }

    private static ClosedCandle Candle(int open, decimal close)
    {
        return History.Candle(open, close);
    }

    private static Dictionary<string, ClosedCandle> Pairs(params (string Symbol, ClosedCandle Candle)[] pairs)
    {
        return pairs.ToDictionary(pair => pair.Symbol, pair => pair.Candle, StringComparer.Ordinal);
    }
}
