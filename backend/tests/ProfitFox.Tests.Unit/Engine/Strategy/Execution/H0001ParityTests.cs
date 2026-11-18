using ProfitFox.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Execution;

/// <summary>
///     The signal of H0001 written as a graph gives the events of <c>event.py</c> of the Python bench on the same candles and funding
///     payments. Events only: no outcome of an H0001 event is computed.
/// </summary>
[TestFixture]
public class H0001ParityTests
{
    [Test]
    public void The_graph_gives_the_events_of_the_python_bench()
    {
        var fixture = ParityFixture.Load();
        Signal signal = CompositeNodeReader.ReadSignal(Graphs.H0001).Value!;

        List<(string, DateTimeOffset)> events = Events(signal, fixture);

        Assert.That(fixture.Events, Is.Not.Empty);
        Assert.That(events, Is.EquivalentTo(fixture.Events));
    }

    [Test]
    public void Another_threshold_gives_other_events()
    {
        var fixture = ParityFixture.Load();
        Signal signal = CompositeNodeReader.ReadSignal(Graphs.H0001.Replace("0.0005", "0.0004", StringComparison.Ordinal)).Value!;

        List<(string, DateTimeOffset)> events = Events(signal, fixture);

        Assert.That(events, Is.Not.EquivalentTo(fixture.Events));
    }

    private static List<(string, DateTimeOffset)> Events(Signal signal, ParityFixture fixture)
    {
        return Feed.Run(signal.Graph, fixture.Candles, fixture.Payments)
            .Where(result => result.Outputs[signal.Output].Boolean)
            .Select(result => (result.InstrumentId, result.CloseTime))
            .ToList();
    }
}