using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.ValueObjects.Execution;

namespace ProfitFox.Tests.Unit.Domain.Services.Nodes;

/// <summary>
///     Change add-setup-strategies, task 2.8: <c>candles</c>, a number of candles as a duration of the timeframe the graph
///     runs on (spec strategy-execution «Срок в свечах»). The refusal <c>plan</c> of a count of 1.5 is in
///     <c>PreviewTests</c>.
/// </summary>
public sealed class CandlesNodeTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void A_day_of_hourly_candles()
    {
        Assert.That(Duration("24", TimeSpan.FromHours(1)), Is.EqualTo(TimeSpan.FromHours(24)));
    }

    [Test]
    public void The_same_graph_on_five_minutes()
    {
        Assert.That(Duration("24", TimeSpan.FromMinutes(5)), Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [TestCase("1.5")]
    [TestCase("0")]
    [TestCase("-2")]
    public void A_count_that_is_not_a_whole_number_of_at_least_one_is_missing(string count)
    {
        Assert.That(Duration(count, TimeSpan.FromHours(1)), Is.Null);
    }

    [Test]
    public void A_count_past_the_range_of_a_duration_is_missing()
    {
        Assert.That(Duration("2000000000", TimeSpan.FromDays(1)), Is.Null);
    }

    private static TimeSpan? Duration(string count, TimeSpan timeframe)
    {
        string json = NodeGraphs.Graph("duration", "n.duration", $$"""{ "id": "n", "kind": "candles", "inputs": { "count": {{count}} } }""");
        var run = new GraphRun(Execution.History.Graph(json));
        var candle = new ClosedCandle(Start, Start + timeframe, 1, 1, 1, 1, 1);
        return run.Step(candle, facts: null)[0].Duration;
    }
}
