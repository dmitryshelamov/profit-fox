using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.Services.Strategies.Validation;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.Strategies;

public class StrategySnapshotTests
{
    private static readonly CustomNodeId Outer = new CustomNodeId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly CustomNodeId Inner = new CustomNodeId(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    [Test]
    public void Snapshot_of_ladder_holds_the_built_in_ladder_node()
    {
        StrategyDefinition ladder = StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder"));

        var snapshot = StrategySnapshot.Of(ladder, BuiltInComposites.Catalog);

        SnapshotComposite composite = snapshot.Composites.ShouldHaveSingleItem();
        composite.Id.ShouldBe(new BuiltInNodeId(BuiltInNode.Ladder));
        composite.Definition.Inputs.ShouldBe(BuiltInComposites.Catalog.Find(composite.Id)!.Inputs);
        snapshot.Catalog().Find(new BuiltInNodeId(BuiltInNode.Ladder)).ShouldBe(composite.Definition);
        snapshot.Catalog().Find(new BuiltInNodeId(BuiltInNode.Dca)).ShouldBeNull();
    }

    [Test]
    public void Snapshot_holds_nodes_used_through_other_nodes_ordered_by_id()
    {
        StrategyDefinition strategy = StrategyDefinitions.Long(new GraphBuilder().Composite("outer", Outer).Build());

        var snapshot = StrategySnapshot.Of(strategy, Catalog(Nested(), Doubler(2m)));

        snapshot.Composites.Select(composite => composite.Id).ShouldBe([Inner, Outer]);
    }

    [Test]
    public void Built_in_nodes_come_before_custom_ones()
    {
        StrategyDefinition strategy = StrategyDefinitions.Long(new GraphBuilder()
            .Composite("custom", Inner)
            .Composite("dca", new BuiltInNodeId(BuiltInNode.Dca))
            .Composite("cross", new BuiltInNodeId(BuiltInNode.EmaCross))
            .Build());
        var catalog = new CompositeCatalog(new Dictionary<CompositeNodeId, CompositeDefinition>
        {
            [Inner] = Doubler(2m),
            [new BuiltInNodeId(BuiltInNode.Dca)] = BuiltInComposites.Catalog.Find(new BuiltInNodeId(BuiltInNode.Dca))!,
            [new BuiltInNodeId(BuiltInNode.EmaCross)] = BuiltInComposites.Catalog.Find(new BuiltInNodeId(BuiltInNode.EmaCross))!
        });

        var snapshot = StrategySnapshot.Of(strategy, catalog);

        snapshot.Composites.Select(composite => composite.Id).ShouldBe([new BuiltInNodeId(BuiltInNode.EmaCross), new BuiltInNodeId(BuiltInNode.Dca), Inner]);
    }

    [Test]
    public void Snapshots_that_differ_only_in_layout_and_name_are_equal()
    {
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        StrategyDefinition moved = ema with
        {
            Graph = ema.Graph with { Layout = new Dictionary<string, NodePosition> { ["params"] = new NodePosition(10m, 20m) } }
        };

        var snapshot = StrategySnapshot.Of(moved, BuiltInComposites.Catalog);

        snapshot.ShouldBe(StrategySnapshot.Of(ema, BuiltInComposites.Catalog));
        snapshot.GetHashCode().ShouldBe(StrategySnapshot.Of(ema, BuiltInComposites.Catalog).GetHashCode());
        snapshot.Definition.Graph.Layout.ShouldBeEmpty();
    }

    [Test]
    public void Snapshots_with_another_input_value_differ()
    {
        StrategyDefinition strategy = StrategyDefinitions.Long(new GraphBuilder().Composite("outer", Outer).Build());

        var snapshot = StrategySnapshot.Of(strategy, Catalog(Nested(), Doubler(2m)));

        snapshot.ShouldNotBe(StrategySnapshot.Of(strategy, Catalog(Nested(), Doubler(3m))));
    }

    [TestCase("direction")]
    [TestCase("parameter")]
    [TestCase("wire")]
    [TestCase("node")]
    [TestCase("settings")]
    [TestCase("input")]
    [TestCase("composite pin")]
    [TestCase("composite node")]
    public void Snapshots_differ_in_anything_that_affects_execution(string change)
    {
        StrategyDefinition strategy = Strategy(1m);
        CompositeDefinition inner = Doubler(2m);
        StrategyDefinition changed = change switch
        {
            "direction" => strategy with { Direction = StrategyDirection.Short },
            "parameter" => strategy with { Parameters = [new StrategyParameter("p", PinType.Number, Values.Number(2m))] },
            "wire" => strategy with { Graph = strategy.Graph with { Wires = [] } },
            "node" => strategy with { Graph = strategy.Graph with { Nodes = [.. strategy.Graph.Nodes.Take(2)] } },
            "settings" => Strategy(1m, CompareOperation.Less),
            "input" => Strategy(5m),
            _ => strategy
        };
        CompositeDefinition changedInner = change switch
        {
            "composite pin" => inner with { Inputs = [new PinDeclaration("z", PinType.Number)] },
            "composite node" => inner with { Graph = inner.Graph with { Nodes = [.. inner.Graph.Nodes.Take(2)] } },
            _ => inner
        };

        var snapshot = StrategySnapshot.Of(changed, Catalog(Nested(), changedInner));

        snapshot.ShouldNotBe(StrategySnapshot.Of(strategy, Catalog(Nested(), inner)));
    }

    [Test]
    public void Layout_inside_a_custom_node_does_not_count()
    {
        StrategyDefinition strategy = StrategyDefinitions.Long(new GraphBuilder().Composite("outer", Outer).Build());
        CompositeDefinition moved = Doubler(2m) with { Graph = Doubler(2m).Graph with { Layout = new Dictionary<string, NodePosition> { ["mul"] = new NodePosition(1m, 1m) } } };

        var snapshot = StrategySnapshot.Of(strategy, Catalog(Nested(), moved));

        snapshot.ShouldBe(StrategySnapshot.Of(strategy, Catalog(Nested(), Doubler(2m))));
    }

    private static StrategyDefinition Strategy(decimal threshold, CompareOperation operation = CompareOperation.Greater)
    {
        return StrategyDefinitions.Long(
            new GraphBuilder()
                .Node("params", SimpleNodeKind.Parameters)
                .Node("cmp", SimpleNodeKind.Compare, new NodeSettings(Operation: operation), ("b", Values.Number(threshold)))
                .Composite("outer", Outer)
                .Wire("params.p", "cmp.a")
                .Build(),
            new StrategyParameter("p", PinType.Number, Values.Number(1m)));
    }

    private static CompositeCatalog Catalog(CompositeDefinition outer, CompositeDefinition inner)
    {
        return new CompositeCatalog(new Dictionary<CompositeNodeId, CompositeDefinition> { [Outer] = outer, [Inner] = inner });
    }

    private static CompositeDefinition Nested()
    {
        return new CompositeDefinition([], [], new GraphBuilder().Composite("inner", Inner, ("x", Values.Number(1m))).Build());
    }

    private static CompositeDefinition Doubler(decimal factor)
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("y", PinType.Number)],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("mul", SimpleNodeKind.Multiply, ("b", Values.Number(factor)))
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.x", "mul.a")
                .Wire("mul.result", "out.y")
                .Build());
    }
}