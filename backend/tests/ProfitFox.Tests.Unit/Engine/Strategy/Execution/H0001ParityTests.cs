using System.Globalization;
using ProfitFox.Domain.Execution;
using ProfitFox.Domain.Graph;
using ProfitFox.Tests.Unit.Engine.Strategy.Graph;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Execution;

/// <summary>
///     Task 4.3: the H0001 graph gives the same events as <c>event.py</c> of the Python bench on the same data. The
///     fixture (<c>Fixtures/H0001</c>, exported from the Python bench <c>profit-fox-bench</c> at commit <c>13bd168</c> by
///     a script removed by task 9.1, in commit <c>349db8b</c>) has real funding payments of January and
///     February 2026, a generated grid of 4h bars with the bars <c>event.py</c> drops already left out, and its events.
/// </summary>
public sealed class H0001ParityTests
{
    private static readonly string Folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Engine", "Strategy", "Fixtures", "H0001");

    [Test]
    public void The_graph_gives_the_events_of_event_py()
    {
        Dictionary<string, List<FundingPayment>> payments = Rows("funding.csv")
            .GroupBy(row => row[0], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new FundingPayment(Time(row[1]), decimal.Parse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture)))
                    .OrderBy(payment => payment.Time)
                    .ToList(),
                StringComparer.Ordinal);
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        var executor = new SignalExecutor(StrategyGraph.ReadSignal(Graphs.H0001).Value!);
        List<(string, DateTimeOffset)> events = [];

        foreach (string[] row in Rows("bars.csv"))
        {
            string instrument = row[0];
            var candle = new ClosedCandle(Time(row[1]), Time(row[2]), 0, 0, 0, 0, 0);
            List<FundingPayment> paid = payments.GetValueOrDefault(instrument) ?? [];
            int index = next.GetValueOrDefault(instrument);
            while (index < paid.Count && paid[index].Time <= candle.CloseTime)
            {
                index++;
            }

            next[instrument] = index;
            if (executor.Step(instrument, candle, index > 0 ? paid[index - 1] : null) is { } found)
            {
                events.Add((found.Instrument, found.Moment));
            }
        }

        List<(string, DateTimeOffset)> expected = [.. Rows("events.csv").Select(row => (row[0], Time(row[1])))];
        Assert.That(expected, Has.Count.GreaterThan(100));
        Assert.That(events.OrderBy(e => e.Item2).ThenBy(e => e.Item1, StringComparer.Ordinal),
            Is.EqualTo(expected.OrderBy(e => e.Item2).ThenBy(e => e.Item1, StringComparer.Ordinal)));
    }

    private static IEnumerable<string[]> Rows(string file)
    {
        return File.ReadLines(Path.Combine(Folder, file)).Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
