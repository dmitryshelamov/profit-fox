using System.Globalization;

using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Market;

/// <summary>Decision 4: the bound of coverage instead of a search for holes.</summary>
public class ScannerPairTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2024-03-10T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly ScannerPair Covered = new ScannerPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), true, CoveredUntil: T, OpenInterestCoveredUntil: T.AddMinutes(1));

    [Test]
    public void Minutes_that_come_one_after_another_move_the_coverage()
    {
        Covered.WithClosedMinute(T).WithClosedMinute(T.AddMinutes(1)).CoveredUntil.ShouldBe(T.AddMinutes(2));
    }

    [Test]
    public void Minute_after_a_gap_does_not_move_the_coverage()
    {
        Covered.WithClosedMinute(T.AddMinutes(1)).CoveredUntil.ShouldBe(T);
    }

    [Test]
    public void Load_from_inside_the_coverage_moves_it_to_the_end_of_the_load()
    {
        Covered.WithLoaded(T.AddMinutes(-5), T.AddMinutes(3)).CoveredUntil.ShouldBe(T.AddMinutes(3));
    }

    [Test]
    public void Load_after_a_gap_does_not_move_the_coverage()
    {
        Covered.WithLoaded(T.AddMinutes(1), T.AddMinutes(3)).CoveredUntil.ShouldBe(T);
    }

    [Test]
    public void Load_older_than_the_coverage_keeps_it()
    {
        Covered.WithLoaded(T.AddMinutes(-60), T.AddMinutes(-30)).CoveredUntil.ShouldBe(T);
    }

    [Test]
    public void First_load_of_a_pair_starts_its_coverage()
    {
        (Covered with { CoveredUntil = null }).WithLoaded(T.AddDays(-1), T).CoveredUntil.ShouldBe(T);
    }

    // The points of open interest are 5 minutes apart: coverage ends at the next step that is not known.
    [Test]
    public void Load_of_open_interest_covers_up_to_the_next_step_after_it()
    {
        ScannerPair pair = Covered with { OpenInterestCoveredUntil = T.AddMinutes(-4) };

        pair.WithOpenInterestLoaded(T.AddMinutes(-4), T.AddMinutes(3)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(6));
        (pair with { OpenInterestCoveredUntil = null }).WithOpenInterestLoaded(T.AddDays(-1), T.AddMinutes(1)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(1));
        pair.WithOpenInterestLoaded(T.AddMinutes(1), T.AddMinutes(3)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(-4));
        pair.WithOpenInterestLoaded(T.AddMinutes(-60), T.AddMinutes(-30)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(-4));
    }

    [Test]
    public void Point_of_open_interest_at_the_bound_moves_it_by_a_step()
    {
        Covered.WithOpenInterestPoint(T.AddMinutes(1)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(6));
        Covered.WithOpenInterestPoint(T.AddMinutes(6)).OpenInterestCoveredUntil.ShouldBe(T.AddMinutes(1));
    }

    // Requirement "Пропуск после перерыва": the row of a pair whose coverage lags behind is incomplete.
    [Test]
    public void Pair_whose_coverage_lags_behind_the_last_closed_minute_is_behind()
    {
        Covered.IsBehind(T).ShouldBeFalse();
        Covered.IsBehind(T.AddMinutes(1)).ShouldBeTrue();
        (Covered with { CoveredUntil = null }).IsBehind(T).ShouldBeTrue();
    }
}