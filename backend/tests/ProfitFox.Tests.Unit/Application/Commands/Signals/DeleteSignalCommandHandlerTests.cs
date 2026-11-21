using ProfitFox.Domain.Exceptions;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Spec signal-constructor: the removal of a saved signal (task 2.1), on a repository in memory.
/// </summary>
public sealed class DeleteSignalCommandHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalRequests _signals = null!;

    [SetUp]
    public void Create()
    {
        _signals = new SignalRequests();
    }

    [Test]
    public async Task Delete_removes_the_signal_from_the_list()
    {
        await _signals.Create("price-drop", null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);

        await _signals.Delete("price-drop", _none);

        Assert.That(await _signals.List(_none), Is.Empty);
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Delete("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }
}
