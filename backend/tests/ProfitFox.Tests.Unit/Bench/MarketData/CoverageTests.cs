using ProfitFox.Domain.MarketData;

namespace ProfitFox.Tests.Unit.Bench.MarketData;

public sealed class CoverageTests
{
    private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Touching_ranges_are_joined_and_cut_to_the_window()
    {
        TimeRange[] ranges = [new TimeRange(Day.AddDays(1), Day.AddDays(2)), new TimeRange(Day, Day.AddDays(1)), new TimeRange(Day.AddDays(3), Day.AddDays(4))];

        IReadOnlyList<TimeRange> trimmed = Coverage.Trim(ranges, Day.AddHours(6), Day.AddDays(3).AddHours(1));

        Assert.That(trimmed, Is.EqualTo(new[] { new TimeRange(Day.AddHours(6), Day.AddDays(2)), new TimeRange(Day.AddDays(3), Day.AddDays(3).AddHours(1)) }));
    }

    [Test]
    public void A_window_across_a_hole_is_not_covered()
    {
        TimeRange[] ranges = [new TimeRange(Day, Day.AddHours(10)), new TimeRange(Day.AddHours(11), Day.AddDays(1))];

        Assert.That(Coverage.Covers(ranges, Day, Day.AddDays(1)), Is.False);
        Assert.That(Coverage.Covers(ranges, Day.AddHours(11), Day.AddDays(1)), Is.True);
    }
}
