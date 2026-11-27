using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Services.Signals;
using ProfitFox.Domain.ValueObjects.Signals;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Spec signal-constructor: the name of a signal and saving only a graph without errors (task 2.1), on a repository in memory.
///     The versions and the database are tested with the repository in the integration tests.
/// </summary>
public sealed class CreateSignalCommandHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalRequests _signals = null!;

    [SetUp]
    public void Create()
    {
        _signals = new SignalRequests();
    }

    [TestCase("Funding High")]
    [TestCase("Funding")]
    [TestCase("funding--high")]
    [TestCase("-funding")]
    [TestCase("../funding")]
    [TestCase("")]
    public async Task A_name_against_the_rule_is_refused_and_nothing_is_written(string name)
    {
        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Create(name, null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => item.Field), Has.Member("name"));
        Assert.That(await _signals.List(_none), Is.Empty);
    }

    [Test]
    public void A_name_of_65_characters_is_refused()
    {
        Assert.That(SignalNames.IsValid(new string('a', 64)), Is.True);
        Assert.That(SignalNames.IsValid(new string('a', 65)), Is.False);
    }

    [Test]
    public async Task A_taken_name_is_refused_on_create_and_rename_and_both_signals_stay()
    {
        await _signals.Create("funding-high", null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);
        await _signals.Create("price-drop", "второй", SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);

        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Create("funding-high", null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That(Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Rename("price-drop", "funding-high", _none))!.Problem, Is.EqualTo(SignalProblem.NameTaken));
        Assert.That((await _signals.List(_none)).Select(entry => entry.Name), Is.EquivalentTo(new[] { "funding-high", "price-drop" }));
    }

    [Test]
    public async Task The_layout_keeps_only_nodes_of_the_graph()
    {
        await _signals.Create("funding-high", null, SignalRequests.H0001Graph(), new Dictionary<string, NodePosition> { ["rate"] = new(0, 0), ["gone"] = new(1, 1) }, _none);

        Assert.That((await _signals.Read("funding-high", _none)).Layout.Keys, Is.EqualTo(new[] { "rate" }));
    }
}
