using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Market;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Tests.Shared.Fixtures.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Market;

/// <summary>Requirement "Метрика без полного окна пуста".</summary>
public class MarketMetricsGapsTests
{
    private static readonly DateTimeOffset End = MarketFixtures.End;

    // Scenario "Пара в первые часы загрузки".
    [Test]
    public void Window_older_than_the_covered_minutes_is_empty_instead_of_a_shorter_one()
    {
        IReadOnlyList<MarketMinute> lastTwoHours = [.. MarketFixtures.Day().TakeLast(120)];

        PairMetrics metrics = MarketMetrics.Compute(End, End.AddHours(-2), End, lastTwoHours, [], MarketFixtures.Ticker(MarketCategory.Spot));

        metrics.Change5m.ShouldBe(3m);
        metrics.Change15m.ShouldBe(28.75m);
        metrics.Change1h.ShouldBe(-17.6m);
        metrics.Turnover1h.ShouldBe(120000m);
        metrics.Change4h.ShouldBeNull();
        metrics.Change24h.ShouldBeNull();
        metrics.Turnover24h.ShouldBeNull();
    }

    // Decision 4: a pair whose coverage lags behind the last closed minute misses its newest minutes.
    [Test]
    public void Every_window_is_empty_while_the_last_minute_is_not_covered()
    {
        PairMetrics metrics = MarketMetrics.Compute(End, End.AddDays(-1), End.AddMinutes(-1), MarketFixtures.Day(), [], MarketFixtures.Ticker(MarketCategory.Spot));

        metrics.Change5m.ShouldBeNull();
        metrics.Change24h.ShouldBeNull();
        metrics.Turnover1h.ShouldBeNull();
        metrics.Turnover24h.ShouldBeNull();
    }

    [Test]
    public void Pair_without_coverage_has_no_window()
    {
        PairMetrics metrics = MarketMetrics.Compute(End, null, null, [], [], MarketFixtures.Ticker(MarketCategory.Spot));

        metrics.Change5m.ShouldBeNull();
        metrics.Turnover1h.ShouldBeNull();
        metrics.Price.ShouldBe(100.25m);
    }

    // A missing minute of the covered range had no trades: zero turnover at the close of the minute before.
    [Test]
    public void Minute_without_trades_counts_as_zero_turnover_at_the_close_before()
    {
        PairMetrics metrics = MarketMetrics.Compute(End, End.AddHours(-1), End, MarketFixtures.Quiet(), [], MarketFixtures.Ticker(MarketCategory.Spot));

        // The first of the last five minutes opens at the close 10 minutes back, 202; the last closes at the close 3 minutes back, 212.1.
        metrics.Change5m.ShouldBe(5m);
        metrics.Turnover1h.ShouldBe(80m);
    }

    [Test]
    public void Window_whose_first_minute_has_no_close_before_it_is_empty()
    {
        PairMetrics metrics = MarketMetrics.Compute(End, End.AddHours(-1), End, MarketFixtures.Quiet(), [], MarketFixtures.Ticker(MarketCategory.Spot));

        metrics.Change1h.ShouldBeNull();
    }

    [Test]
    public void Change_of_open_interest_without_the_point_an_hour_back_is_empty()
    {
        IReadOnlyList<OpenInterestPoint> points = [.. MarketFixtures.OpenInterest().Where(point => point.Time != End.AddHours(-1))];

        PairMetrics metrics = MarketMetrics.Compute(End, End.AddDays(-1), End, MarketFixtures.Day(), points, MarketFixtures.Ticker(MarketCategory.Linear));

        metrics.OpenInterestChange1h.ShouldBeNull();
        metrics.OpenInterestChange24h.ShouldBe(-20m);
    }

    // The point of the current 5 minutes is the newest one; the minute need not be the first of the 5.
    [Test]
    public void Open_interest_changes_from_the_point_of_the_current_five_minutes()
    {
        PairMetrics metrics = MarketMetrics.Compute(End.AddMinutes(3), null, null, [], MarketFixtures.OpenInterest(), MarketFixtures.Ticker(MarketCategory.Linear));

        metrics.OpenInterestChange1h.ShouldBe(25m);
        metrics.OpenInterestChange24h.ShouldBe(-20m);
    }

    // A pair listed a moment ago may have a point of open interest of 0 an hour back; a zero never divides.
    [Test]
    public void Change_from_zero_open_interest_is_empty()
    {
        IReadOnlyList<OpenInterestPoint> points = [.. MarketFixtures.OpenInterest().Select(point => point.Time == End.AddHours(-1) ? point with { Value = 0m } : point)];

        PairMetrics metrics = MarketMetrics.Compute(End, End.AddDays(-1), End, MarketFixtures.Day(), points, MarketFixtures.Ticker(MarketCategory.Linear));

        metrics.OpenInterestChange1h.ShouldBeNull();
        metrics.OpenInterestChange24h.ShouldBe(-20m);
    }

    [Test]
    public void Spread_of_an_empty_book_is_empty()
    {
        MarketTicker ticker = MarketFixtures.Ticker(MarketCategory.Spot) with { Bid = 0m, Ask = 0m };

        PairMetrics metrics = MarketMetrics.Compute(End, End.AddDays(-1), End, MarketFixtures.Day(), [], ticker);

        metrics.Spread.ShouldBeNull();
        metrics.Change5m.ShouldBe(3m);
    }
}