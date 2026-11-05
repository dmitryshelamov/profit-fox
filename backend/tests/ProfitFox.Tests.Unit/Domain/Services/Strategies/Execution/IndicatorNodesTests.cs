using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Strategies.Execution;

public class IndicatorNodesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    [Test]
    public void Ema_starts_from_the_simple_average_and_follows_each_close()
    {
        StrategyRun run = Ema(3m);

        Values(run, 1m, 2m, 3m, 4m).ShouldBe([null, null, 2m, 3m]);
    }

    [Test]
    public void Ema_matches_the_reference_series()
    {
        StrategyRun run = Ema(3m);

        Values(run, 10m, 11m, 12m, 13m, 14m, 13m, 12m, 11m, 12m, 13m)
            .ShouldBe([null, null, 11m, 12m, 13m, 13m, 12.5m, 11.75m, 11.875m, 12.4375m]);
    }

    [Test]
    public void Indicator_without_enough_history_has_no_value_and_compares_false()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema", SimpleNodeKind.Ema, ("period", Tests.Shared.Builders.Strategies.Values.Number(5m)))
            .Node("cmp", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.LessOrEqual), ("a", Tests.Shared.Builders.Strategies.Values.Number(0m)))
            .Wire("candle.close", "ema.source")
            .Wire("ema.value", "cmp.b")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);

        Close(run, 1m);
        Close(run, 2m);
        Close(run, 3m);

        run.ValueOf("ema", "value").ShouldBeNull();
        run.ValueOf("cmp", "result").ShouldBe(new BooleanValue(false));
    }

    [Test]
    public void Ema_is_updated_only_by_closed_bars()
    {
        StrategyRun run = Ema(3m);
        Values(run, 1m, 2m, 3m);

        run.Handle(new RunStarted(Start));

        run.ValueOf("ema", "value").ShouldBe(new NumberValue(2m));
    }

    [Test]
    public void Ema_with_a_period_below_one_has_no_value()
    {
        StrategyRun run = Ema(0m);

        Values(run, 1m, 2m).ShouldBe([null, null]);
    }

    [Test]
    public void Ema_without_a_period_or_a_source_has_no_value()
    {
        Graph noPeriod = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("position", SimpleNodeKind.Position)
            .Node("ema", SimpleNodeKind.Ema)
            .Wire("candle.close", "ema.source")
            .Wire("position.averagePrice", "ema.period")
            .Build();
        Graph noSource = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("ema", SimpleNodeKind.Ema, ("period", Tests.Shared.Builders.Strategies.Values.Number(1m)))
            .Wire("position.averagePrice", "ema.source")
            .Build();

        foreach (Graph graph in new[] { noPeriod, noSource })
        {
            var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
            Close(run, 1m);
            run.ValueOf("ema", "value").ShouldBeNull();
        }
    }

    // Scenario "Эталонные значения SMA".
    [Test]
    public void Sma_is_the_simple_average_of_the_last_period_closes()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Sma, 3m);

        Values(run, "window", 1m, 2m, 3m, 4m).ShouldBe([null, null, 2m, 3m]);
    }

    [Test]
    public void Sma_takes_no_missing_input_into_its_window()
    {
        Graph graph = new GraphBuilder()
            .Node("position", SimpleNodeKind.Position)
            .Node("window", SimpleNodeKind.Sma, ("period", Tests.Shared.Builders.Strategies.Values.Number(1m)))
            .Wire("position.averagePrice", "window.source")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);

        Values(run, "window", 1m, 2m).ShouldBe([null, null]);
    }

    [Test]
    public void Sma_is_updated_only_by_closed_bars()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Sma, 2m);
        Values(run, "window", 1m, 3m);

        run.Handle(new RunStarted(Start));

        run.ValueOf("window", "value").ShouldBe(new NumberValue(2m));
    }

    [Test]
    public void Sma_with_a_period_below_one_has_no_value()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Sma, 0m);

        Values(run, "window", 1m, 2m).ShouldBe([null, null]);
    }

    // Scenario "Эталонные значения отклонения".
    [Test]
    public void Std_dev_is_the_population_deviation_of_the_last_period_closes()
    {
        StrategyRun run = Windowed(SimpleNodeKind.StdDev, 2m);

        Values(run, "window", 1m, 3m, 7m).ShouldBe([null, 1m, 2m]);
    }

    // Scenario "Ровное окно".
    [Test]
    public void Std_dev_of_a_flat_window_is_zero()
    {
        StrategyRun run = Windowed(SimpleNodeKind.StdDev, 3m);

        Values(run, "window", 5m, 5m, 5m).ShouldBe([null, null, 0m]);
    }

    // Scenario "Эталонные значения RSI": the seed averages are 0.5 and 0.5, then Wilder smoothing gives 1.25 and 0.25.
    [Test]
    public void Rsi_seeds_with_simple_averages_and_then_smooths_like_Wilder()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Rsi, 2m);

        List<decimal?> values = Values(run, "window", 10m, 11m, 10m, 12m);

        values.Take(3).ShouldBe([null, null, 50m]);
        Math.Round(values[3]!.Value, 10).ShouldBe(83.3333333333m);
    }

    // Scenario "Только рост".
    [Test]
    public void Rsi_of_only_gains_is_one_hundred()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Rsi, 2m);

        Values(run, "window", 1m, 2m, 3m).ShouldBe([null, null, 100m]);
    }

    [Test]
    public void Rsi_of_only_losses_is_zero()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Rsi, 2m);

        Values(run, "window", 3m, 2m, 1m).ShouldBe([null, null, 0m]);
    }

    [Test]
    public void Rsi_without_changes_is_fifty()
    {
        StrategyRun run = Windowed(SimpleNodeKind.Rsi, 2m);

        Values(run, "window", 5m, 5m, 5m).ShouldBe([null, null, 50m]);
    }

    // Scenario "Сдвиг на один бар".
    [Test]
    public void Previous_gives_the_input_of_the_previous_closed_bar()
    {
        StrategyRun run = Previous();

        Values(run, "previous", 7m, 9m, 4m).ShouldBe([null, 7m, 9m]);
    }

    [Test]
    public void Previous_without_a_value_at_the_previous_close_has_no_value()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema", SimpleNodeKind.Ema, ("period", Tests.Shared.Builders.Strategies.Values.Number(2m)))
            .Node("previous", SimpleNodeKind.Previous)
            .Wire("candle.close", "ema.source")
            .Wire("ema.value", "previous.value")
            .Build();
        var run = StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);

        Values(run, "previous", 1m, 3m, 5m).ShouldBe([null, null, 2m]);
    }

    // Scenario "Событие ордера".
    [Test]
    public void Previous_is_not_moved_by_an_order_event()
    {
        StrategyRun run = Previous();
        Values(run, "previous", 7m, 9m, 4m);

        run.Handle(new OrderCancelled(Start, "o-1"));

        run.ValueOf("previous", "value").ShouldBe(new NumberValue(9m));
    }

    [Test]
    public void Cross_up_on_the_bar_where_a_rises_above_b()
    {
        StrategyRun run = Cross();

        Close(run, 9m);
        run.ValueOf("cross", "up").ShouldBe(new BooleanValue(false));

        Close(run, 11m);
        run.ValueOf("cross", "up").ShouldBe(new BooleanValue(true));
        run.ValueOf("cross", "down").ShouldBe(new BooleanValue(false));

        Close(run, 12m);
        run.ValueOf("cross", "up").ShouldBe(new BooleanValue(false));
    }

    [Test]
    public void Cross_down_on_the_bar_where_a_falls_below_b()
    {
        StrategyRun run = Cross();

        Close(run, 10m);
        Close(run, 9m);

        run.ValueOf("cross", "down").ShouldBe(new BooleanValue(true));
        run.ValueOf("cross", "up").ShouldBe(new BooleanValue(false));
    }

    [Test]
    public void Cross_signal_lasts_only_its_bar_event()
    {
        StrategyRun run = Cross();
        Close(run, 9m);
        Close(run, 11m);

        run.Handle(new RunStarted(Start));

        run.ValueOf("cross", "up").ShouldBe(new BooleanValue(false));
    }

    private static StrategyRun Ema(decimal period)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("ema", SimpleNodeKind.Ema, ("period", Tests.Shared.Builders.Strategies.Values.Number(period)))
            .Wire("candle.close", "ema.source")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static StrategyRun Windowed(SimpleNodeKind kind, decimal period)
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("window", kind, ("period", Tests.Shared.Builders.Strategies.Values.Number(period)))
            .Wire("candle.close", "window.source")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static StrategyRun Previous()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("previous", SimpleNodeKind.Previous)
            .Wire("candle.close", "previous.value")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static StrategyRun Cross()
    {
        Graph graph = new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Node("cross", SimpleNodeKind.Cross, ("b", Tests.Shared.Builders.Strategies.Values.Number(10m)))
            .Wire("candle.close", "cross.a")
            .Build();

        return StrategyRun.Start(StrategyDefinitions.Long(graph), CompositeCatalog.Empty, new Dictionary<string, NodeValue>(), Conditions.Neutral);
    }

    private static List<decimal?> Values(StrategyRun run, params decimal[] closes)
    {
        return Values(run, "ema", closes);
    }

    private static List<decimal?> Values(StrategyRun run, string node, params decimal[] closes)
    {
        return [.. closes.Select(close => Close(run, close)).Select(_ => (run.ValueOf(node, "value") as NumberValue)?.Value)];
    }

    private static int Close(StrategyRun run, decimal close)
    {
        run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));

        return 0;
    }
}