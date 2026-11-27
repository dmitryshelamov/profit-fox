using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;
using ProfitFox.Tests.Unit.Application.Commands.Strategies;

namespace ProfitFox.Tests.Unit.Application.Queries.Signals;

/// <summary>
///     Spec signal-constructor «Стратегии сигнала в списке» in the list of the saved signals (change redesign-bench-ui, task
///     2.2), on repositories in memory: each signal names the strategies any version of which points at it.
/// </summary>
public sealed class SignalStrategiesTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private StrategyRequests _strategies = null!;

    [SetUp]
    public void Create()
    {
        _strategies = new StrategyRequests();
    }

    [Test]
    public async Task A_signal_with_a_strategy()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        SignalEntryInternal entry = (await _strategies.Signals.List(_none)).Single();

        Assert.That(entry.Name, Is.EqualTo("funding-high"));
        Assert.That(entry.Strategies, Is.EqualTo(new[] { "funding-short" }));
    }

    [Test]
    public async Task A_reference_only_in_an_earlier_version_of_the_strategy()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);
        await _strategies.Store.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        SignalEntryInternal entry = (await _strategies.Signals.List(_none)).Single();

        Assert.That(entry.Strategies, Is.EqualTo(new[] { "funding-short" }), "the strategies the refusal of a delete names");
    }

    [Test]
    public async Task A_signal_without_references()
    {
        SignalDocument used = await _strategies.SaveSignal();
        await _strategies.SaveSignal("candle-drop");
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(used.Id)), _none);

        IReadOnlyList<SignalEntryInternal> list = await _strategies.Signals.List(_none);

        Assert.That(list.Single(entry => entry.Name == "candle-drop").Strategies, Is.Empty);
    }

    [Test]
    public async Task The_strategies_of_a_signal_are_in_name_order()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("zeta", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);
        await _strategies.Create("alpha", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        SignalEntryInternal entry = (await _strategies.Signals.List(_none)).Single();

        Assert.That(entry.Strategies, Is.EqualTo(new[] { "alpha", "zeta" }));
    }

    [Test]
    public async Task A_signal_that_does_not_read_has_no_strategies()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);
        _strategies.SignalStore.Break("funding-high", "the node cmp points at an output that is not there");

        SignalEntryInternal entry = (await _strategies.Signals.List(_none)).Single();

        Assert.That((entry.Error, entry.Id), Is.EqualTo(("the node cmp points at an output that is not there", (Guid?)null)));
        Assert.That(entry.Strategies, Is.Empty);
    }

    [Test]
    public async Task The_strategies_are_read_once_for_the_whole_list()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.SaveSignal("candle-drop");
        await _strategies.SaveSignal("price-drop");
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        await _strategies.Signals.List(_none);

        Assert.That(_strategies.Store.UsageReads, Is.EqualTo(1));
    }
}
