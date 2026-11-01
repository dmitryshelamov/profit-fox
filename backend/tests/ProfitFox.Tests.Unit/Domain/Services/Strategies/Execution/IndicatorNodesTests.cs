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
        return [.. closes.Select(close => Close(run, close)).Select(_ => (run.ValueOf("ema", "value") as NumberValue)?.Value)];
    }

    private static int Close(StrategyRun run, decimal close)
    {
        run.Handle(new BarClosed(Start, new Bar(close, close, close, close, 1m)));

        return 0;
    }
}