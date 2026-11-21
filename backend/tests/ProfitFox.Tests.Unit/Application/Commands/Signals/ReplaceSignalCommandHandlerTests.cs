using System.Text.Json.Nodes;

using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Spec signal-constructor: a replaced signal is saved only with a graph without errors (task 2.1), on a repository in memory.
/// </summary>
public sealed class ReplaceSignalCommandHandlerTests
{
    private readonly CancellationToken _none = CancellationToken.None;

    private SignalRequests _signals = null!;

    [SetUp]
    public void Create()
    {
        _signals = new SignalRequests();
    }

    [Test]
    public async Task A_graph_with_an_error_is_refused_and_the_saved_signal_stays()
    {
        await _signals.Create("funding-high", null, SignalRequests.H0001Graph(), SignalRequests.NoLayout, _none);
        JsonNode broken = SignalRequests.H0001GraphNode();
        broken["wires"]!.AsArray().RemoveAt(1);

        SignalLibraryException error = Assert.ThrowsAsync<SignalLibraryException>(() => _signals.Replace("funding-high", null, SignalRequests.Element(broken), SignalRequests.NoLayout, _none))!;

        Assert.That(error.Problem, Is.EqualTo(SignalProblem.Invalid));
        Assert.That(error.Errors.Select(item => (item.Code, item.Node, item.Pin)), Is.EqualTo(new[] { (GraphErrorCode.MissingInput, (string?)"high", (string?)"b") }));
        Assert.That((await _signals.Read("funding-high", _none)).Graph.Nodes, Has.Count.EqualTo(5));
    }
}
