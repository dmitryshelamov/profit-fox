using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.BuiltIns;

public class BuiltInCompositesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Ladder и DCA не встроены": the built-in nodes are parts of strategies, not whole strategies.
    [Test]
    public void Built_in_composites_are_ema_cross_macd_and_bollinger()
    {
        BuiltInComposites.All.Select(composite => (composite.Node, composite.Title)).ShouldBe(
        [
            (BuiltInNode.EmaCross, "Пересечение EMA"),
            (BuiltInNode.Macd, "MACD"),
            (BuiltInNode.Bollinger, "Полосы Боллинджера")
        ]);
    }

    [Test]
    public void Every_built_in_composite_is_valid()
    {
        BuiltInComposites.All.ShouldAllBe(composite =>
            GraphValidator.ValidateComposite(new BuiltInNodeId(composite.Node), composite.Definition, BuiltInComposites.Catalog).Count == 0);
    }

    [Test]
    public void Ema_cross_takes_its_source_and_periods_as_inputs()
    {
        Inputs(BuiltInNode.EmaCross).ShouldBe("source:Number fast:Number slow:Number");
    }

    [Test]
    public void Ema_cross_signals_on_the_reference_closes()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("cross", new BuiltInNodeId(BuiltInNode.EmaCross), ("fast", Values.Number(2m)), ("slow", Values.Number(3m)))
            .Wire("candle.close", "cross.source")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), BuiltInComposites.Catalog, new Dictionary<string, NodeValue>(), Conditions.Neutral);
        var signals = new List<string>();

        foreach (decimal close in new[] { 10m, 9m, 8m, 7m, 8m, 10m, 12m, 11m, 9m, 7m })
        {
            run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
            bool up = run.ValueOf("cross/out", "up") is BooleanValue { Value: true };
            bool down = run.ValueOf("cross/out", "down") is BooleanValue { Value: true };
            signals.Add(up ? "up" : down ? "down" : "-");
        }

        signals.ShouldBe(["-", "-", "-", "-", "-", "up", "-", "-", "down", "-"]);
    }

    // Scenario "MACD".
    [Test]
    public void Macd_takes_its_source_and_periods_and_gives_the_line_the_signal_and_the_histogram()
    {
        Inputs(BuiltInNode.Macd).ShouldBe("source:Number fast:Number slow:Number signal:Number");
        Outputs(BuiltInNode.Macd).ShouldBe("macd:Number signal:Number histogram:Number");
    }

    // Reference closes with exact values: the fast EMA of period 1 is the close, the slow one and the signal have a step of 1/2.
    [Test]
    public void Macd_gives_the_line_the_signal_and_the_histogram_on_the_reference_closes()
    {
        StrategyRun run = Composite(BuiltInNode.Macd, ("fast", 1m), ("slow", 3m), ("signal", 3m));
        var rows = new List<(decimal? Macd, decimal? Signal, decimal? Histogram)>();

        foreach (decimal close in new[] { 106m, 112m, 106m, 106m, 110m, 106m, 112m, 102m })
        {
            Close(run, close);
            rows.Add((Value(run, "macd"), Value(run, "signal"), Value(run, "histogram")));
        }

        rows.ShouldBe(
        [
            (null, null, null),
            (null, null, null),
            (-2m, null, null),
            (-1m, null, null),
            (1.5m, -0.5m, 2m),
            (-1.25m, -0.875m, -0.375m),
            (2.375m, 0.75m, 1.625m),
            (-3.8125m, -1.53125m, -2.28125m)
        ]);
    }

    // Scenario "Сигнальная линия MACD".
    [Test]
    public void Macd_line_appears_on_the_third_bar_and_the_signal_and_the_histogram_on_the_fourth()
    {
        StrategyRun run = Composite(BuiltInNode.Macd, ("fast", 2m), ("slow", 3m), ("signal", 2m));
        var present = new List<string>();

        foreach (decimal close in new[] { 10m, 12m, 11m, 13m, 12m })
        {
            Close(run, close);
            present.Add(string.Concat(new[] { "macd", "signal", "histogram" }.Select(pin => Value(run, pin) is null ? "-" : "+")));
        }

        present.ShouldBe(["---", "---", "+--", "+++", "+++"]);
    }

    // Scenario "Полосы Боллинджера".
    [Test]
    public void Bollinger_takes_its_source_period_and_width_and_gives_the_three_bands()
    {
        Inputs(BuiltInNode.Bollinger).ShouldBe("source:Number period:Number width:Number");
        Outputs(BuiltInNode.Bollinger).ShouldBe("upper:Number middle:Number lower:Number");
    }

    [Test]
    public void Bollinger_bands_lie_a_width_of_deviations_around_the_average_on_the_reference_closes()
    {
        StrategyRun run = Composite(BuiltInNode.Bollinger, ("period", 2m), ("width", 2m));
        var rows = new List<(decimal? Upper, decimal? Middle, decimal? Lower)>();

        foreach (decimal close in new[] { 10m, 14m, 12m, 20m })
        {
            Close(run, close);
            rows.Add((Value(run, "upper"), Value(run, "middle"), Value(run, "lower")));
        }

        rows.ShouldBe([(null, null, null), (16m, 12m, 8m), (15m, 13m, 11m), (24m, 16m, 8m)]);
    }

    [Test]
    public void Bollinger_bands_of_a_flat_window_coincide_with_the_average()
    {
        StrategyRun run = Composite(BuiltInNode.Bollinger, ("period", 2m), ("width", 2m));

        Close(run, 5m);
        Close(run, 5m);

        (Value(run, "upper"), Value(run, "middle"), Value(run, "lower")).ShouldBe((5m, 5m, 5m));
    }

    /// <summary>The built-in node on the closes of a candle, its inputs but the source set to numbers.</summary>
    private static StrategyRun Composite(BuiltInNode node, params (string Pin, decimal Value)[] inputs)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("node", new BuiltInNodeId(node), [.. inputs.Select(input => (input.Pin, (NodeValue)Values.Number(input.Value)))])
            .Wire("candle.close", "node.source")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), BuiltInComposites.Catalog, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static void Close(StrategyRun run, decimal close)
    {
        run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));
    }

    private static decimal? Value(StrategyRun run, string pin)
    {
        return (run.ValueOf("node/out", pin) as NumberValue)?.Value;
    }

    private static string Outputs(BuiltInNode node)
    {
        CompositeDefinition definition = BuiltInComposites.All.Single(composite => composite.Node == node).Definition;

        return string.Join(" ", definition.Outputs.Select(pin => $"{pin.Name}:{pin.Type}"));
    }

    private static string Inputs(BuiltInNode node)
    {
        CompositeDefinition definition = BuiltInComposites.All.Single(composite => composite.Node == node).Definition;

        return string.Join(" ", definition.Inputs.Select(pin => $"{pin.Name}:{pin.Type}"));
    }
}