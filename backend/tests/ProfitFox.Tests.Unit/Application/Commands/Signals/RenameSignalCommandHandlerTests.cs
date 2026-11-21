using ProfitFox.Domain.Exceptions;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Spec signal-constructor: the rename of a saved signal (task 2.1), on a repository in memory.
/// </summary>
public sealed class RenameSignalCommandHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalRequests _signals = null!;

    [SetUp]
    public void Create()
    {
        _signals = new SignalRequests();
    }

    [Test]
    public async Task Rename_gives_the_signal_its_new_name_and_keeps_the_rest()
    {
        await _signals.Create("price-drop", "описание", SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);

        await _signals.Rename("price-drop", "price-fall", _none);

        Assert.That((await _signals.Read("price-fall", _none)).Description, Is.EqualTo("описание"));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Read("price-drop", _none))!.Problem, Is.EqualTo(SignalProblem.NotFound));
    }
}
