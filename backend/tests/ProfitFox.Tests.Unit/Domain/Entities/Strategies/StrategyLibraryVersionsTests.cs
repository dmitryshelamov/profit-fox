using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies;

public class StrategyLibraryVersionsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Scenario "Первая версия".
    [Test]
    public void New_strategy_gets_version_one_with_its_graph_and_the_built_in_node()
    {
        var library = new StrategyLibrary(0, [], []);
        StrategyDefinition ladder = Fixture("ladder");

        library.AddStrategy(Id, "Ladder", ladder, Now);

        StrategyVersion version = library.LatestVersion(Id)!;
        version.ShouldBe(new StrategyVersion(Id, 1, StrategySnapshot.Of(ladder, BuiltInComposites.Catalog), Now));
        version.Snapshot.Composites.ShouldHaveSingleItem().Id.ShouldBe(new BuiltInNodeId(BuiltInNode.Ladder));
        library.NewVersions.ShouldBe([version]);
    }

    // Scenario "Изменён граф".
    [Test]
    public void Changed_input_value_gives_the_next_version()
    {
        StrategyLibrary library = Saved("ema", out StrategyVersion first);
        StrategyDefinition ema = Fixture("ema");
        GraphNode enter = ema.Graph.Nodes.Single(node => node.Id == "enter");
        StrategyDefinition changed = ema with
        {
            Graph = ema.Graph with
            {
                Nodes = [.. ema.Graph.Nodes.Select(node => node == enter ? node with { Inputs = new Dictionary<string, NodeValue> { ["size"] = new NumberValue(5m) } } : node)],
                Wires = [.. ema.Graph.Wires.Where(wire => wire.To != new PinRef("enter", "size"))]
            }
        };

        library.UpdateStrategy(Id, "EMA", changed, Now.AddHours(1));

        StrategyVersion version = library.LatestVersion(Id)!;
        version.ShouldBe(new StrategyVersion(Id, 2, StrategySnapshot.Of(changed, BuiltInComposites.Catalog), Now.AddHours(1)));
        library.NewVersions.ShouldBe([version]);
        first.Number.ShouldBe(1);
    }

    // Scenario "Изменены только координаты".
    [Test]
    public void Changed_name_and_layout_give_no_version()
    {
        StrategyLibrary library = Saved("ema", out StrategyVersion first);
        StrategyDefinition ema = Fixture("ema");
        StrategyDefinition moved = ema with { Graph = ema.Graph with { Layout = new Dictionary<string, NodePosition>() } };

        library.UpdateStrategy(Id, "EMA 2", moved, Now.AddHours(1));

        library.LatestVersion(Id).ShouldBe(first);
        library.NewVersions.ShouldBeEmpty();
    }

    // Scenario "Правка узла создаёт версию стратегии".
    [Test]
    public void Compatible_edit_of_a_nested_node_gives_a_version_of_the_strategy_and_keeps_the_old_one()
    {
        var ladderId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var crossId = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(crossId, "Моё пересечение", Doubler(2m), Now);
        library.AddComposite(ladderId, "Моя лесенка", Uses(crossId), Now);
        library.AddStrategy(Id, "S", StrategyDefinitions.Long(new GraphBuilder().Composite("ladder", new CustomNodeId(ladderId)).Build()), Now);
        StrategyVersion old = library.LatestVersion(Id)!;

        library.UpdateComposite(crossId, "Моё пересечение", Doubler(3m), Now.AddHours(1));

        StrategyVersion version = library.LatestVersion(Id)!;
        version.Number.ShouldBe(2);
        version.CreatedAt.ShouldBe(Now.AddHours(1));
        version.Snapshot.Catalog().Find(new CustomNodeId(crossId))!.Graph.Nodes.Single(node => node.Id == "mul").Inputs["b"].ShouldBe(Values.Number(3m));
        old.Snapshot.Catalog().Find(new CustomNodeId(crossId))!.Graph.Nodes.Single(node => node.Id == "mul").Inputs["b"].ShouldBe(Values.Number(2m));
        library.NewVersions.ShouldBe([old, version]);
    }

    [Test]
    public void Edit_of_a_node_the_strategy_does_not_use_gives_no_version()
    {
        var unused = Guid.Parse("00000000-0000-0000-0000-00000000000c");
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(unused, "Чужой узел", Doubler(2m), Now);
        library.AddStrategy(Id, "EMA", Fixture("ema"), Now);

        library.UpdateComposite(unused, "Чужой узел", Doubler(3m), Now.AddHours(1));

        library.LatestVersion(Id)!.Number.ShouldBe(1);
    }

    [Test]
    public void Unknown_strategy_has_no_version()
    {
        new StrategyLibrary(0, [], []).LatestVersion(Id).ShouldBeNull();
    }

    /// <summary>A library loaded with the strategy at version 1.</summary>
    private static StrategyLibrary Saved(string fixture, out StrategyVersion version)
    {
        StrategyDefinition definition = Fixture(fixture);
        version = new StrategyVersion(Id, 1, StrategySnapshot.Of(definition, BuiltInComposites.Catalog), Now);

        return new StrategyLibrary(1, [new Strategy(Id, fixture, definition, Now, Now)], [], [version]);
    }

    private static CompositeDefinition Uses(Guid node)
    {
        return new CompositeDefinition([], [], new GraphBuilder().Composite("cross", new CustomNodeId(node), ("x", Values.Number(1m))).Build());
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

    private static StrategyDefinition Fixture(string name)
    {
        return StrategyJson.ParseStrategy(StrategyFixtures.Element(name));
    }
}