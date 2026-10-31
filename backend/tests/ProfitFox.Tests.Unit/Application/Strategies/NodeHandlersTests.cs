using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Strategies;
using ProfitFox.Application.Strategies.Commands.CloneNode;
using ProfitFox.Application.Strategies.Commands.CreateNode;
using ProfitFox.Application.Strategies.Commands.DeleteNode;
using ProfitFox.Application.Strategies.Commands.UpdateNode;
using ProfitFox.Application.Strategies.Queries.GetNode;
using ProfitFox.Application.Strategies.Queries.GetNodeCatalog;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Strategies;

public class NodeHandlersTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");
    private static readonly Guid Signal = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private IStrategyLibraryRepository _repository = null!;
    private StrategyLibrary _library = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Now);
        _library = new StrategyLibrary(3, [], []);
        _library.AddComposite(Signal, "Мой сигнал", SignalDefinition(), Now.AddDays(-1));
        _library.AddStrategy(User, "EMA", UsesSignal(), Now.AddDays(-1));
        _repository = Substitute.For<IStrategyLibraryRepository>();
        _repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _library);
        _repository.SaveCompositeAsync(Arg.Any<CompositeNode>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.DeleteCompositeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.GetCompositesAsync(Arg.Any<CancellationToken>()).Returns(_ => _library.Composites);
        _repository.GetCompositeAsync(Signal, Arg.Any<CancellationToken>()).Returns(_ => _library.Composites[0]);
    }

    [Test]
    public async Task Valid_composite_is_saved()
    {
        Guid id = await new CreateNodeCommandHandler(_repository, _time).Handle(new CreateNodeCommand("Новый", Json(SignalDefinition())), CancellationToken.None);

        await _repository.Received(1).SaveCompositeAsync(Arg.Is<CompositeNode>(node => node.Id == id && node.Name == "Новый"), 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Built_in_node_cannot_be_changed_or_deleted()
    {
        Should.Throw<StateConflictException>(() =>
            new UpdateNodeCommandHandler(_repository, _time).Handle(new UpdateNodeCommand("ladder", "x", Json(SignalDefinition())), CancellationToken.None));
        Should.Throw<StateConflictException>(() => new DeleteNodeCommandHandler(_repository).Handle(new DeleteNodeCommand("ladder"), CancellationToken.None));
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [TestCase("nope")]
    [TestCase("00000000-0000-0000-0000-0000000000ff")]
    public void Unknown_node_is_not_found(string id)
    {
        Should.Throw<NotFoundException>(() =>
            new UpdateNodeCommandHandler(_repository, _time).Handle(new UpdateNodeCommand(id, "x", Json(SignalDefinition())), CancellationToken.None));
        Should.Throw<NotFoundException>(() => new DeleteNodeCommandHandler(_repository).Handle(new DeleteNodeCommand(id), CancellationToken.None));
        Should.Throw<NotFoundException>(() => new CloneNodeCommandHandler(_repository, _time).Handle(new CloneNodeCommand(id, null), CancellationToken.None));
    }

    [Test]
    public async Task Compatible_change_is_saved()
    {
        await new UpdateNodeCommandHandler(_repository, _time).Handle(new UpdateNodeCommand(Signal.ToString(), "Сигнал 2", Json(SignalDefinition())), CancellationToken.None);

        await _repository.Received(1).SaveCompositeAsync(Arg.Is<CompositeNode>(node => node.Id == Signal && node.Name == "Сигнал 2" && node.UpdatedAt == Now), 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Breaking_change_is_a_conflict_with_the_usages()
    {
        var withoutOutput = new CompositeDefinition([new PinDeclaration("x", PinType.Number)], [], new GraphBuilder().Build());

        StateConflictException exception = Should.Throw<StateConflictException>(() =>
            new UpdateNodeCommandHandler(_repository, _time).Handle(new UpdateNodeCommand(Signal.ToString(), "x", Json(withoutOutput)), CancellationToken.None));

        exception.Usages.ShouldHaveSingleItem().Name.ShouldBe("EMA");
    }

    [Test]
    public void Invalid_composite_graph_is_rejected_with_paths()
    {
        CompositeDefinition broken = SignalDefinition() with { Graph = new GraphBuilder().Node("mul", SimpleNodeKind.Multiply).Build() };

        ValidationFailedException exception = Should.Throw<ValidationFailedException>(() =>
            new UpdateNodeCommandHandler(_repository, _time).Handle(new UpdateNodeCommand(Signal.ToString(), "x", Json(broken)), CancellationToken.None));

        exception.Errors.Keys.ShouldContain("graph.mul.a");
    }

    [Test]
    public void Used_node_cannot_be_deleted()
    {
        StateConflictException exception = Should.Throw<StateConflictException>(() =>
            new DeleteNodeCommandHandler(_repository).Handle(new DeleteNodeCommand(Signal.ToString()), CancellationToken.None));

        exception.Usages.ShouldHaveSingleItem().Id.ShouldBe(User);
    }

    [Test]
    public async Task Unused_node_is_deleted()
    {
        _library.RemoveStrategy(User);

        await new DeleteNodeCommandHandler(_repository).Handle(new DeleteNodeCommand(Signal.ToString()), CancellationToken.None);

        await _repository.Received(1).DeleteCompositeAsync(Signal, 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Clone_takes_the_source_name_with_a_mark_unless_a_name_is_given()
    {
        var handler = new CloneNodeCommandHandler(_repository, _time);

        Guid ladder = await handler.Handle(new CloneNodeCommand("ladder", null), CancellationToken.None);
        Guid named = await handler.Handle(new CloneNodeCommand(Signal.ToString(), "Свой"), CancellationToken.None);
        Guid custom = await handler.Handle(new CloneNodeCommand(Signal.ToString(), null), CancellationToken.None);

        await _repository.Received(1).SaveCompositeAsync(Arg.Is<CompositeNode>(node => node.Id == ladder && node.Name == "Ladder (копия)"), 3, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveCompositeAsync(Arg.Is<CompositeNode>(node => node.Id == named && node.Name == "Свой"), 3, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveCompositeAsync(Arg.Is<CompositeNode>(node => node.Id == custom && node.Name == "Мой сигнал (копия)"), 3, Arg.Any<CancellationToken>());
    }

    [TestCase("")]
    [TestCase("  ")]
    public void Clone_with_an_empty_name_is_rejected(string name)
    {
        Should.Throw<ValidationFailedException>(() => new CloneNodeCommandHandler(_repository, _time).Handle(new CloneNodeCommand("ladder", name), CancellationToken.None))
            .Errors.Keys.ShouldBe(["name"]);
    }

    [Test]
    public void Save_on_a_changed_library_is_a_conflict()
    {
        _repository.SaveCompositeAsync(Arg.Any<CompositeNode>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(false);

        Should.Throw<StateConflictException>(() => new CloneNodeCommandHandler(_repository, _time).Handle(new CloneNodeCommand("dca", null), CancellationToken.None));
    }

    [Test]
    public async Task Catalog_lists_simple_built_in_and_custom_nodes()
    {
        IReadOnlyList<NodeCatalogEntry> catalog = await new GetNodeCatalogQueryHandler(_repository).Handle(new GetNodeCatalogQuery(), CancellationToken.None);

        catalog.Count(entry => entry.Group == NodeGroup.Simple).ShouldBe(Enum.GetValues<SimpleNodeKind>().Length);
        catalog.Single(entry => entry.Id == "ema").Title.ShouldBe("EMA");
        NodeCatalogEntry ladder = catalog.Single(entry => entry.Id == "ladder");
        (ladder.Group, ladder.Title).ShouldBe((NodeGroup.BuiltIn, "Ladder"));
        NodeCatalogEntry custom = catalog.Single(entry => entry.Id == Signal.ToString());
        (custom.Group, custom.Title).ShouldBe((NodeGroup.Custom, "Мой сигнал"));
        custom.Outputs.ShouldHaveSingleItem().Name.ShouldBe("on");
    }

    [Test]
    public async Task Node_is_read_with_its_definition()
    {
        var handler = new GetNodeQueryHandler(_repository);

        NodeResult ladder = (await handler.Handle(new GetNodeQuery("ladder"), CancellationToken.None))!;
        NodeResult ema = (await handler.Handle(new GetNodeQuery("ema"), CancellationToken.None))!;
        NodeResult custom = (await handler.Handle(new GetNodeQuery(Signal.ToString()), CancellationToken.None))!;

        ladder.Definition.ShouldBe(BuiltInComposites.All.Single(composite => composite.Node == BuiltInNode.Ladder).Definition);
        (ema.Entry.Group, ema.Definition).ShouldBe((NodeGroup.Simple, null));
        custom.Definition.ShouldBe(_library.Composites[0].Definition);
        (await handler.Handle(new GetNodeQuery("nope"), CancellationToken.None)).ShouldBeNull();
        (await handler.Handle(new GetNodeQuery(Guid.Empty.ToString()), CancellationToken.None)).ShouldBeNull();
    }

    private static System.Text.Json.JsonElement Json(CompositeDefinition definition)
    {
        return System.Text.Json.JsonDocument.Parse(StrategyJson.WriteComposite(definition).ToJsonString()).RootElement;
    }

    private static CompositeDefinition SignalDefinition()
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("on", PinType.Boolean)],
            new GraphBuilder()
                .Node("in", SimpleNodeKind.Inputs)
                .Node("above", SimpleNodeKind.Compare, new NodeSettings(Operation: CompareOperation.Greater), ("b", Values.Number(1m)))
                .Node("out", SimpleNodeKind.Outputs)
                .Wire("in.x", "above.a")
                .Wire("above.result", "out.on")
                .Build());
    }

    private static StrategyDefinition UsesSignal()
    {
        return StrategyDefinitions.Long(new GraphBuilder()
            .Node("candle", SimpleNodeKind.Candle)
            .Composite("signal", new CustomNodeId(Signal))
            .Node("not", SimpleNodeKind.Not)
            .Wire("candle.close", "signal.x")
            .Wire("signal.on", "not.value")
            .Build());
    }
}