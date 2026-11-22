using System.Text.Json;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;
using ProfitFox.Tests.Unit.Application.Commands.Strategies;

namespace ProfitFox.Tests.Unit.Application.Queries.Strategies;

/// <summary>
///     Spec strategy-constructor «Сигналы и выходы стратегии в списке» and «Отметка новой версии сигнала в списке
///     стратегий» in the list of the saved strategies (change redesign-bench-ui, task 2.3), on repositories in memory.
/// </summary>
public sealed class StrategyLinksTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private StrategyRequests _strategies = null!;

    [SetUp]
    public void Create()
    {
        _strategies = new StrategyRequests();
    }

    [Test]
    public async Task A_signal_and_a_term()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(entry.Signals, Is.EqualTo(new[] { new StrategySignalInternal("funding-high", Version: 1, LatestVersion: 1) }));
        Assert.That(entry.Exits, Is.EqualTo(new[] { "close-after" }));
    }

    [Test]
    public async Task A_stop_and_a_target()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await Save("breakout-long", Graph(
            Signal("s", signal.Id, version: 1),
            """{ "id": "enter", "kind": "enter", "settings": { "side": "long" } }""",
            """{ "id": "target", "kind": "take-profit", "inputs": { "distance": 0.02 } }""",
            """{ "id": "stop", "kind": "stop-loss", "inputs": { "distance": 0.01 } }"""));

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(entry.Exits, Is.EqualTo(new[] { "stop-loss", "take-profit" }), "in the order stop, target, term, whatever the order of the nodes");
    }

    [Test]
    public async Task Two_nodes_of_one_signal()
    {
        SignalDocument signal = await _strategies.SaveSignal(version: 2);
        await Save("funding-twice", Graph(Signal("a", signal.Id, version: 2), Signal("b", signal.Id, version: 2)));

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(entry.Signals, Is.EqualTo(new[] { new StrategySignalInternal("funding-high", Version: 2, LatestVersion: 2) }));
    }

    [Test]
    public async Task A_strategy_without_signals()
    {
        await Save("funding-short", StrategyDocumentGenerator.GenerateGraph(signal: null));

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(entry.Signals, Is.Empty);
        Assert.That(entry.Exits, Is.EqualTo(new[] { "close-after" }));
    }

    [Test]
    public async Task A_newer_version_of_the_signal()
    {
        SignalDocument signal = await _strategies.SaveSignal("volume-breakout", version: 4);
        await Save("breakout-trail", StrategyDocumentGenerator.GenerateGraph(signal.Id, signalVersion: 3));

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();
        StrategyInternal read = await _strategies.Read("breakout-trail", _none);

        Assert.That(entry.Signals, Is.EqualTo(new[] { new StrategySignalInternal("volume-breakout", Version: 3, LatestVersion: 4) }));
        Assert.That(read.Signals.Single().Version, Is.EqualTo(3), "the list does not move the reference");
    }

    [Test]
    public async Task The_signals_are_in_the_order_of_name_and_version()
    {
        SignalDocument volume = await _strategies.SaveSignal("volume-breakout", version: 2);
        SignalDocument funding = await _strategies.SaveSignal("funding-high", version: 2);
        await Save("mixed", Graph(Signal("a", volume.Id, version: 1), Signal("b", funding.Id, version: 2), Signal("c", funding.Id, version: 1)));

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(
            entry.Signals.Select(use => (use.Name, use.Version)),
            Is.EqualTo(new[] { ("funding-high", 1), ("funding-high", 2), ("volume-breakout", 1) }));
    }

    [Test]
    public async Task The_versions_of_the_signals_are_read_once_for_the_whole_list()
    {
        SignalDocument funding = await _strategies.SaveSignal();
        SignalDocument volume = await _strategies.SaveSignal("volume-breakout");
        await Save("funding-short", StrategyDocumentGenerator.GenerateGraph(funding.Id));
        await Save("breakout-long", StrategyDocumentGenerator.GenerateGraph(volume.Id));
        await Save("plain", StrategyDocumentGenerator.GenerateGraph(signal: null));

        IReadOnlyList<StrategyEntryInternal> list = await _strategies.List(_none);

        Assert.That(_strategies.SignalStore.VersionReads, Is.EqualTo(1));
        Assert.That(list.Select(entry => (entry.Name, Signal: entry.Signals.SingleOrDefault()?.Name)), Is.EqualTo(new[]
        {
            ("breakout-long", "volume-breakout"), ("funding-short", "funding-high"), ("plain", (string?)null)
        }));
    }

    [Test]
    public async Task A_strategy_that_does_not_read_has_neither_signals_nor_exits()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await Save("funding-short", StrategyDocumentGenerator.GenerateGraph(signal.Id));
        _strategies.Store.Break("funding-short", "the layout does not read");

        StrategyEntryInternal entry = (await _strategies.List(_none)).Single();

        Assert.That(entry.Error, Is.EqualTo("the layout does not read"));
        Assert.That(entry.Signals, Is.Empty);
        Assert.That(entry.Exits, Is.Empty);
    }

    // Kept as written, without the checks of a strategy: the list reads the graph of the latest version only by its form.
    private Task Save(string name, GraphDefinition graph)
    {
        return _strategies.Store.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name, graph: graph), _none);
    }

    private static string Signal(string id, Guid signal, int version)
    {
        return $$"""{ "id": "{{id}}", "kind": "signal", "settings": { "signal": "{{signal}}", "version": "{{version}}" } }""";
    }

    private static GraphDefinition Graph(params string[] nodes)
    {
        using var document = JsonDocument.Parse($$"""{ "formatVersion": 1, "nodes": [{{string.Join(", ", nodes)}}], "wires": [] }""");
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }
}
