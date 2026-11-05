using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirement "Зоны поддержки и сопротивления" on the candles of <see cref="IdeaFixtures.Zones" />.</summary>
public class ZoneFinderTests
{
    // Scenario "Две поддержки".
    [Test]
    public void Close_minimums_make_a_zone_and_a_lone_one_is_no_zone()
    {
        IReadOnlyList<Zone> zones = ZoneFinder.Find(IdeaFixtures.Zones("two-supports"), 1m, ZoneRules.Default);

        zones.ShouldBe([new Zone(100.0m, 100.4m, 4)]);
        ZoneFinder.Support(zones, 103.5m).ShouldBe(new Zone(100.0m, 100.4m, 4));
    }

    // A touch is an extreme against two candles on each side; touches within 0.5 ATR of the lowest of a zone join it. Only the last 200
    // candles count.
    [Test]
    public void Zones_of_a_long_history_come_from_its_last_200_candles()
    {
        ZoneFinder.Find(IdeaFixtures.Zones("zones"), 1.2m, ZoneRules.Default).ShouldBe(
        [
            new Zone(45.20m, 45.32m, 2), new Zone(46.09m, 46.23m, 2), new Zone(47.38m, 47.97m, 4), new Zone(48.15m, 48.32m, 2),
            new Zone(49.76m, 50.26m, 3), new Zone(50.47m, 50.83m, 3), new Zone(51.51m, 52.02m, 3), new Zone(52.14m, 52.64m, 3),
            new Zone(53.55m, 54.10m, 4), new Zone(54.25m, 54.74m, 4), new Zone(55.03m, 55.24m, 2), new Zone(55.82m, 56.42m, 4),
            new Zone(56.64m, 57.00m, 3), new Zone(58.29m, 58.58m, 4)
        ]);
    }

    // The zone below the price is a support, above it a resistance; the nearest of each is the closest to the price.
    [Test]
    public void Nearest_support_lies_below_the_price_and_nearest_resistance_above_it()
    {
        IReadOnlyList<Zone> zones = ZoneFinder.Find(IdeaFixtures.Zones("zones"), 1.2m, ZoneRules.Default);

        ZoneFinder.Support(zones, 57.55m).ShouldBe(new Zone(56.64m, 57.00m, 3));
        ZoneFinder.Resistance(zones, 57.55m).ShouldBe(new Zone(58.29m, 58.58m, 4));
    }

    // A zone the price stands in is neither.
    [Test]
    public void A_zone_around_the_price_is_neither_support_nor_resistance()
    {
        Zone[] zones = [new Zone(99m, 101m, 2)];

        ZoneFinder.Support(zones, 100m).ShouldBeNull();
        ZoneFinder.Resistance(zones, 100m).ShouldBeNull();
    }
}