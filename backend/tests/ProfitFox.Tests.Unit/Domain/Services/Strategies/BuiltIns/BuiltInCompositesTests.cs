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

    // Scenario "Ladder и DCA не встроены".
    [Test]
    public void Ema_cross_is_the_only_built_in_composite()
    {
        BuiltInComposites.All.Select(composite => (composite.Node, composite.Title)).ShouldBe([(BuiltInNode.EmaCross, "Пересечение EMA")]);
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

    private static string Inputs(BuiltInNode node)
    {
        CompositeDefinition definition = BuiltInComposites.All.Single(composite => composite.Node == node).Definition;

        return string.Join(" ", definition.Inputs.Select(pin => $"{pin.Name}:{pin.Type}"));
    }
}