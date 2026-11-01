using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies;

public class StrategyLibraryCompositesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly Guid SignalId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid WrapperId = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid StrategyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CloneId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    [Test]
    public void Valid_composite_is_added()
    {
        var library = new StrategyLibrary(0, [], []);

        CompositeNode node = library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);

        node.ShouldBe(new CompositeNode(SignalId, "Мой сигнал", node.Definition, Now, Now));
        library.Composites.ShouldBe([node]);
        library.Catalog().Find(new CustomNodeId(SignalId)).ShouldBe(node.Definition);
    }

    [Test]
    public void Composite_with_errors_is_not_added()
    {
        var library = new StrategyLibrary(0, [], []);
        CompositeDefinition broken = Signal(100m) with { Graph = new GraphBuilder().Node("mul", SimpleNodeKind.Multiply).Build() };

        Should.Throw<GraphValidationException>(() => library.AddComposite(SignalId, "Мой сигнал", broken, Now));

        library.Composites.ShouldBeEmpty();
    }

    [Test]
    public void Compatible_edit_reaches_the_strategy_that_uses_the_composite()
    {
        StrategyLibrary library = LibraryWithStrategy();
        Buys(library, 150m).ShouldBeTrue();

        library.UpdateComposite(SignalId, "Мой сигнал", Signal(200m), Now.AddHours(1));

        Buys(library, 150m).ShouldBeFalse();
    }

    [Test]
    public void Edit_that_breaks_a_strategy_is_rejected_with_the_usage()
    {
        StrategyLibrary library = LibraryWithStrategy();
        CompositeDefinition withoutOutput = WithoutOutput();

        NodeInUseException exception = Should.Throw<NodeInUseException>(() => library.UpdateComposite(SignalId, "Мой сигнал", withoutOutput, Now));

        NodeUsage usage = exception.Usages.ShouldHaveSingleItem();
        (usage.Kind, usage.Id, usage.Name).ShouldBe((UsageKind.Strategy, StrategyId, "EMA"));
        usage.Errors.ShouldContain(new GraphError("signal", "on", "Node has no output on."));
        library.Composites.Single().Definition.Outputs.ShouldHaveSingleItem().Name.ShouldBe("on");
    }

    [Test]
    public void Edit_that_breaks_a_nested_usage_names_every_broken_graph()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);
        library.AddComposite(WrapperId, "Обёртка", Wrapper(), Now);
        library.AddStrategy(StrategyId, "EMA", BuyOn(WrapperId), Now);

        NodeInUseException exception = Should.Throw<NodeInUseException>(() =>
            library.UpdateComposite(SignalId, "Мой сигнал", WithoutOutput(), Now));

        exception.Usages.Select(usage => (usage.Kind, usage.Name)).ShouldBe([(UsageKind.Composite, "Обёртка"), (UsageKind.Strategy, "EMA")]);
    }

    [Test]
    public void Missing_composite_is_not_changed_or_removed()
    {
        var library = new StrategyLibrary(0, [], []);

        library.UpdateComposite(SignalId, "Мой сигнал", Signal(100m), Now).ShouldBeNull();
        library.RemoveComposite(SignalId).ShouldBeFalse();
    }

    [Test]
    public void Used_composite_is_not_removed()
    {
        StrategyLibrary library = LibraryWithStrategy();

        NodeInUseException exception = Should.Throw<NodeInUseException>(() => library.RemoveComposite(SignalId));

        NodeUsage usage = exception.Usages.ShouldHaveSingleItem();
        (usage.Kind, usage.Id, usage.Name).ShouldBe((UsageKind.Strategy, StrategyId, "EMA"));
        usage.Errors.ShouldBeEmpty();
        library.Composites.ShouldHaveSingleItem();
    }

    [Test]
    public void Composite_used_by_another_composite_is_not_removed()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);
        library.AddComposite(WrapperId, "Обёртка", Wrapper(), Now);

        NodeInUseException exception = Should.Throw<NodeInUseException>(() => library.RemoveComposite(SignalId));

        exception.Usages.ShouldHaveSingleItem().Kind.ShouldBe(UsageKind.Composite);
    }

    [Test]
    public void Unused_composite_is_removed()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);

        library.RemoveComposite(SignalId).ShouldBeTrue();

        library.Composites.ShouldBeEmpty();
    }

    [Test]
    public void Built_in_composites_are_in_the_catalog()
    {
        new StrategyLibrary(0, [], []).Catalog().Find(new BuiltInNodeId(BuiltInNode.Ladder)).ShouldNotBeNull();
    }

    [Test]
    public void Clone_of_a_built_in_composite_is_an_independent_custom_node()
    {
        var library = new StrategyLibrary(0, [], []);
        CompositeDefinition ladder = library.Catalog().Find(new BuiltInNodeId(BuiltInNode.Ladder))!;

        CompositeNode clone = library.CloneComposite(new BuiltInNodeId(BuiltInNode.Ladder), CloneId, "Ladder (копия)", Now)!;
        library.UpdateComposite(CloneId, "Моя лесенка", ladder with { Inputs = [.. ladder.Inputs, new PinDeclaration("extra", PinType.Number)] }, Now);

        clone.Definition.ShouldBe(ladder);
        library.Catalog().Find(new BuiltInNodeId(BuiltInNode.Ladder)).ShouldBe(ladder);
        library.Composites.Single().Name.ShouldBe("Моя лесенка");
    }

    [Test]
    public void Clone_of_a_custom_composite_copies_its_definition()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);

        library.CloneComposite(new CustomNodeId(SignalId), CloneId, "Мой сигнал (копия)", Now)!.Definition.ShouldBe(library.Composites[0].Definition);
        library.CloneComposite(new CustomNodeId(Guid.Empty), Guid.NewGuid(), "Нет", Now).ShouldBeNull();
    }

    private static StrategyLibrary LibraryWithStrategy()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(SignalId, "Мой сигнал", Signal(100m), Now);
        library.AddStrategy(StrategyId, "EMA", BuyOn(SignalId), Now);

        return library;
    }

    private static bool Buys(StrategyLibrary library, decimal close)
    {
        var run = StrategyRun.Start(library.Strategies.Single().Definition, library.Catalog(), new Dictionary<string, NodeValue>(), Conditions.Neutral);

        return run.Handle(new BarClosed(Now, new Bar(close, close, close, close, 1m))).Count > 0;
    }

    /// <summary>Says <c>on</c> while <c>x</c> is above <paramref name="threshold" />.</summary>
    private static CompositeDefinition Signal(decimal threshold)
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("on", PinType.Boolean)],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("above", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(threshold)))
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.x", "above.a")
                .Wire("above.result", "out.on")
                .Build());
    }

    private static CompositeDefinition WithoutOutput()
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("above", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(1m)))
                .Wire("in.x", "above.a")
                .Build());
    }

    private static CompositeDefinition Wrapper()
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("on", PinType.Boolean)],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Composite("signal", new CustomNodeId(SignalId))
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.x", "signal.x")
                .Wire("signal.on", "out.on")
                .Build());
    }

    private static StrategyDefinition BuyOn(Guid composite)
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("signal", new CustomNodeId(composite))
            .Node("buy", SimpleNodeKind.MarketOrder, new NodeSettings(Side: OrderSide.Buy, SizeMode: OrderSizeMode.Quantity), ("size", Values.Number(1m)))
            .Wire("candle.close", "signal.x")
            .Wire("signal.on", "buy.when")
            .Build());
    }
}