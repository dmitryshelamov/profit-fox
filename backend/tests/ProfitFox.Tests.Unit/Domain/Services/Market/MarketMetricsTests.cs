using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Market;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Tests.Shared.Fixtures.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Market;

/// <summary>Requirement "Метрики снимка" on the reference windows of <see cref="MarketFixtures" />.</summary>
public class MarketMetricsTests
{
    private static readonly DateTimeOffset End = MarketFixtures.End;

    // Scenario "Изменение за 5 минут": open of the first of the last five minutes 100, close of the last 103.
    [Test]
    public void Change_over_a_window_runs_from_the_open_of_its_first_minute_to_the_close_of_its_last()
    {
        PairMetrics metrics = Day(MarketCategory.Linear);

        metrics.Change5m.ShouldBe(3m);
        metrics.Change15m.ShouldBe(28.75m);
        metrics.Change1h.ShouldBe(-17.6m);
        metrics.Change4h.ShouldBe(106m);
        metrics.Change24h.ShouldBe(-50m);
    }

    [Test]
    public void Turnover_sums_the_minutes_of_the_hour_and_of_the_day()
    {
        PairMetrics metrics = Day(MarketCategory.Linear);

        metrics.Turnover1h.ShouldBe(120000m);
        metrics.Turnover24h.ShouldBe(1500000m);
    }

    [Test]
    public void Price_and_spread_come_from_the_ticker()
    {
        PairMetrics metrics = Day(MarketCategory.Spot);

        metrics.Price.ShouldBe(100.25m);
        metrics.Spread.ShouldBe(1m);
    }

    // Decision 6: open interest is kept in coins, the snapshot turns it into USDT at the last price.
    [Test]
    public void Open_interest_is_in_usdt_and_changes_against_the_points_an_hour_and_a_day_back()
    {
        PairMetrics metrics = Day(MarketCategory.Linear);

        metrics.OpenInterest.ShouldBe(200500m);
        metrics.OpenInterestChange1h.ShouldBe(25m);
        metrics.OpenInterestChange24h.ShouldBe(-20m);
        metrics.FundingRate.ShouldBe(0.0001m);
    }

    // Scenario "Пара spot".
    [Test]
    public void Spot_pair_has_no_open_interest_and_no_funding()
    {
        PairMetrics metrics = MarketMetrics.Compute(End, End.AddDays(-1), End, MarketFixtures.Day(), [], MarketFixtures.Ticker(MarketCategory.Spot));

        metrics.OpenInterest.ShouldBeNull();
        metrics.OpenInterestChange1h.ShouldBeNull();
        metrics.OpenInterestChange24h.ShouldBeNull();
        metrics.FundingRate.ShouldBeNull();
    }

    private static PairMetrics Day(MarketCategory category)
    {
        return MarketMetrics.Compute(
            End,
            End.AddDays(-1),
            End,
            MarketFixtures.Day(),
            category == MarketCategory.Linear ? MarketFixtures.OpenInterest() : [],
            MarketFixtures.Ticker(category));
    }
}