using System.Text.Json.Nodes;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Application.Commands.Strategies;

/// <summary>
///     Spec strategy-constructor «Сохранённая стратегия», «Сохраняется только стратегия без ошибок» and «Создание,
///     переименование, копирование и удаление стратегии» (change add-strategy-constructor, task 4.1), on repositories in
///     memory. The versions and the database are tested with the repository in the integration tests.
/// </summary>
public sealed class StrategyCommandHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private StrategyRequests _strategies = null!;
    private SignalDocument _signal = null!;

    [SetUp]
    public async Task Create()
    {
        _strategies = new StrategyRequests();
        _signal = await _strategies.SaveSignal();
    }

    [Test]
    public async Task A_saved_strategy_keeps_its_signal_node_as_written()
    {
        StrategyInternal created = await _strategies.Create("funding-short", Graph(), _none);

        StrategyInternal read = await _strategies.Read("funding-short", _none);

        Assert.That((created.Name, read.Version), Is.EqualTo(("funding-short", 1)));
        Assert.That(read.Graph.Nodes.Select(node => node.Kind), Is.EqualTo(new[] { "signal", "enter", "close-after" }));
        Assert.That(read.Signals.Single(), Is.EqualTo(created.Signals.Single()));
        Assert.That((read.Signals.Single().Node, read.Signals.Single().Name), Is.EqualTo(("s", "funding-high")));
    }

    [TestCase("Funding Short")]
    [TestCase("funding--short")]
    [TestCase("")]
    public async Task A_name_against_the_rule(string name)
    {
        StrategyLibraryException error = Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Create(name, Graph(), _none))!;

        Assert.That(error.Problem, Is.EqualTo(StrategyProblem.Invalid));
        Assert.That(error.Errors.Single(item => item.Field == "name").Reason, Does.Contain("not a strategy name"));
        Assert.That(await _strategies.List(_none), Is.Empty);
    }

    [Test]
    public async Task One_name_for_a_signal_and_a_strategy()
    {
        await _strategies.Create("funding-high", Graph(), _none);

        Assert.That((await _strategies.List(_none)).Single().Name, Is.EqualTo("funding-high"));
        Assert.That((await _strategies.SignalStore.ReadAsync("funding-high", _none))!.Signal, Is.SameAs(_signal), "the signal did not change");
    }

    [Test]
    public async Task No_close_after_node()
    {
        await _strategies.Create("funding-short", Graph(), _none);
        JsonObject graph = StrategyRequests.Strategy(_signal.Id);
        graph["nodes"]!.AsArray().RemoveAt(2);

        StrategyLibraryException error = Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Replace("funding-short", StrategyRequests.Element(graph), _none))!;

        Assert.That(error.Problem, Is.EqualTo(StrategyProblem.Invalid));
        Assert.That(error.Errors.Single().Code, Is.EqualTo(GraphErrorCode.StrategyCloseAfterMissing));
        Assert.That((await _strategies.Read("funding-short", _none)).Graph.Nodes, Has.Count.EqualTo(3), "the saved strategy stays");
    }

    [Test]
    public void A_reference_to_a_deleted_signal()
    {
        StrategyLibraryException error = Assert.ThrowsAsync<StrategyLibraryException>(() =>
            _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(Guid.NewGuid())), _none))!;

        Assert.That(error.Problem, Is.EqualTo(StrategyProblem.Invalid));
        Assert.That((error.Errors.Single().Code, error.Errors.Single().Node), Is.EqualTo((GraphErrorCode.UnknownSignal, "s")));
    }

    [Test]
    public async Task A_taken_name()
    {
        await _strategies.Create("funding-short", Graph(), _none);
        await _strategies.Create("price-drop-long", Graph(), _none);

        Assert.That(Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Rename("price-drop-long", "funding-short", _none))!.Problem, Is.EqualTo(StrategyProblem.NameTaken));
        Assert.That(Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Create("funding-short", Graph(), _none))!.Problem, Is.EqualTo(StrategyProblem.NameTaken));
        Assert.That((await _strategies.List(_none)).Select(entry => entry.Name), Is.EqualTo(new[] { "funding-short", "price-drop-long" }));
    }

    [Test]
    public async Task Rename_and_delete()
    {
        await _strategies.Create("funding-short", Graph(), _none);

        StrategyInternal renamed = await _strategies.Rename("funding-short", "funding-short-4h", _none);
        await _strategies.Delete("funding-short-4h", _none);

        Assert.That((renamed.Name, renamed.Signals.Single().Name), Is.EqualTo(("funding-short-4h", "funding-high")));
        Assert.That(await _strategies.List(_none), Is.Empty);
        Assert.That(Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Read("funding-short-4h", _none))!.Problem, Is.EqualTo(StrategyProblem.NotFound));
        Assert.That(Assert.ThrowsAsync<StrategyLibraryException>(() => _strategies.Delete("funding-short-4h", _none))!.Problem, Is.EqualTo(StrategyProblem.NotFound));
    }

    [Test]
    public async Task The_layout_keeps_only_nodes_of_the_graph()
    {
        var command = new ProfitFox.Application.Commands.Strategies.CreateStrategy.CreateStrategyCommand(
            "funding-short",
            "  ",
            Graph(),
            new Dictionary<string, ProfitFox.Domain.ValueObjects.Signals.NodePosition> { ["enter"] = new(1, 2), ["gone"] = new(3, 4) });

        StrategyInternal created = await new ProfitFox.Application.Commands.Strategies.CreateStrategy.CreateStrategyCommandHandler(_strategies.Store, _strategies.SignalStore)
            .Handle(command, _none);

        Assert.That(created.Layout.Keys, Is.EqualTo(new[] { "enter" }));
        Assert.That(created.Description, Is.Null, "a blank description is none");
    }

    private System.Text.Json.JsonElement Graph()
    {
        return StrategyRequests.Element(StrategyRequests.Strategy(_signal.Id));
    }
}
