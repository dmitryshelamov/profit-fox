using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.ValueObjects.MarketData;

/// <summary>Decision 4: inside <c>[From, Until)</c> storage holds everything the exchange gave; a candle missing there was not traded.</summary>
public class SeriesCoverageTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2024-03-04T00:00:00Z");
    private static readonly SeriesCoverage Covered = new SeriesCoverage(Day.AddHours(8), Day.AddHours(20));

    [Test]
    public void Touching_range_extends_the_border()
    {
        Covered.Extend(Day.AddHours(20), Day.AddHours(24)).ShouldBe(new SeriesCoverage(Day.AddHours(8), Day.AddHours(24)));
        Covered.Extend(Day.AddHours(4), Day.AddHours(8)).ShouldBe(new SeriesCoverage(Day.AddHours(4), Day.AddHours(20)));
    }

    [Test]
    public void Overlapping_range_extends_the_border()
    {
        Covered.Extend(Day.AddHours(10), Day.AddHours(30)).ShouldBe(new SeriesCoverage(Day.AddHours(8), Day.AddHours(30)));
    }

    [Test]
    public void Range_inside_the_coverage_changes_nothing()
    {
        Covered.Extend(Day.AddHours(10), Day.AddHours(12)).ShouldBe(Covered);
    }

    [Test]
    public void Separate_range_leaves_the_border()
    {
        Covered.Extend(Day.AddHours(22), Day.AddHours(30)).ShouldBe(Covered);
        Covered.Extend(Day.AddHours(1), Day.AddHours(7)).ShouldBe(Covered);
    }

    [Test]
    public void Gap_inside_the_coverage_is_not_missing()
    {
        Covered.Uncovered([(Day.AddHours(12), Day.AddHours(14))]).ShouldBeEmpty();
    }

    [Test]
    public void Gap_partly_outside_the_coverage_is_cut_to_the_outside()
    {
        Covered.Uncovered([(Day.AddHours(18), Day.AddHours(23))]).ShouldBe([(Day.AddHours(20), Day.AddHours(23))]);
        Covered.Uncovered([(Day.AddHours(5), Day.AddHours(10))]).ShouldBe([(Day.AddHours(5), Day.AddHours(8))]);
    }

    [Test]
    public void Gap_around_the_coverage_is_cut_into_two()
    {
        Covered.Uncovered([(Day.AddHours(5), Day.AddHours(23))]).ShouldBe([(Day.AddHours(5), Day.AddHours(8)), (Day.AddHours(20), Day.AddHours(23))]);
    }

    [Test]
    public void Gap_outside_the_coverage_stays()
    {
        Covered.Uncovered([(Day.AddHours(1), Day.AddHours(3)), (Day.AddHours(21), Day.AddHours(22))]).ShouldBe([(Day.AddHours(1), Day.AddHours(3)), (Day.AddHours(21), Day.AddHours(22))]);
    }

    [Test]
    public void Border_must_follow_the_start()
    {
        Should.Throw<ArgumentException>(() => new SeriesCoverage(Day.AddHours(8), Day.AddHours(8)));
    }
}