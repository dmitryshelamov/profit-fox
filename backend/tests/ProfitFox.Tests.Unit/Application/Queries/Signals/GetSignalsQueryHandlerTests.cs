using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Tests.Unit.Application.Commands.Signals;

namespace ProfitFox.Tests.Unit.Application.Queries.Signals;

/// <summary>
///     Spec signal-constructor: the list of the saved signals (task 2.1), on a repository in memory.
/// </summary>
public sealed class GetSignalsQueryHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalRequests _signals = null!;

    [SetUp]
    public void Create()
    {
        _signals = new SignalRequests();
    }

    [Test]
    public async Task The_list_is_in_name_order()
    {
        await _signals.Create("funding-high", null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);
        await _signals.Create("candle-drop", "свеча", SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);

        IReadOnlyList<SignalEntryInternal> list = await _signals.List(_none);

        Assert.That(list.Select(entry => entry.Name), Is.EqualTo(new[] { "candle-drop", "funding-high" }));
        Assert.That(list[0], Is.EqualTo(new SignalEntryInternal("candle-drop", "свеча", Version: 1, Error: null) { Id = list[0].Id }));
        Assert.That(list.Select(entry => entry.Id), Has.None.Null.And.Unique, "a signal that reads has its id");
    }
}
