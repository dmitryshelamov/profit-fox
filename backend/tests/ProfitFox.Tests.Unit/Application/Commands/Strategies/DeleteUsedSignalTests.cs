using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Exceptions;

namespace ProfitFox.Tests.Unit.Application.Commands.Strategies;

/// <summary>
///     Spec signal-constructor «Сигнал, на который ссылается стратегия» in the handler of the delete of a signal (change
///     add-strategy-constructor, task 4.3): the strategies are asked before the delete and named in the refusal.
/// </summary>
public sealed class DeleteUsedSignalTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private StrategyRequests _strategies = null!;

    [SetUp]
    public void Create()
    {
        _strategies = new StrategyRequests();
    }

    [Test]
    public async Task Deleting_a_signal_in_use_is_refused_with_the_names_of_the_strategies()
    {
        SignalDocument signal = await _strategies.SaveSignal();
        await _strategies.Create("funding-short", StrategyRequests.Element(StrategyRequests.Strategy(signal.Id)), _none);

        SignalLibraryException refusal = Assert.ThrowsAsync<SignalLibraryException>(() => _strategies.Signals.Delete("funding-high", _none))!;

        Assert.That(refusal.Problem, Is.EqualTo(SignalProblem.InUse));
        Assert.That(refusal.Strategies, Is.EqualTo(new[] { "funding-short" }));
        Assert.That(refusal.Message, Does.Contain("funding-short"));
        Assert.That(await _strategies.SignalStore.ExistsAsync("funding-high", _none), Is.True);
    }

    [Test]
    public async Task A_signal_without_references_is_deleted_as_before()
    {
        await _strategies.SaveSignal();
        await _strategies.SaveSignal("price-drop");
        SignalDocument other = (await _strategies.SignalStore.ReadAsync("price-drop", _none))!.Signal!;
        await _strategies.Create("price-drop-long", StrategyRequests.Element(StrategyRequests.Strategy(other.Id)), _none);

        await _strategies.Signals.Delete("funding-high", _none);

        Assert.That(await _strategies.SignalStore.NamesAsync(_none), Is.EqualTo(new[] { "price-drop" }));
    }
}
