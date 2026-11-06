using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>Requirements "Калибровка на будущих исходах", "Оценка Брайера калибровки" and "Таблицы калибровки".</summary>
public class CalibrationCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly IdeaBucketKey Linear = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
    private static readonly IdeaBucketKey OtherLinear = Linear with { Setup = IdeaSetup.Drop };
    private static readonly IdeaBucketKey Spot = Linear with { Category = MarketCategory.Spot };

    // Scenario "Закрытый позже случай не подсказывает": B fired before A and closed after it, so A sees only the 30 stops; B sees them too.
    [Test]
    public void Case_closed_after_the_checked_one_fired_is_not_in_its_window()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, Now.AddDays(-80), 30, IdeaStatus.Stop, -1m),
            new IdeaOutcome(Linear, a.AddHours(-1), a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(2), IdeaStatus.Expired, 0.1m)
        ];

        CalibrationReport report = Report(outcomes, 90, null);

        (report.Checked, report.Unchecked).ShouldBe((2, 30));
        // Both were promised 0 %: B reached its target, A expired. Had B told A, A would have been promised 1 / 31.
        report.Brier.ShouldBe(0.5m);
    }

    // Scenario "Старый случай вне окна": the stops fired 95 days before A, the targets 89; A sees only the targets.
    [Test]
    public void Case_fired_more_than_90_days_before_the_checked_one_is_not_in_its_window()
    {
        DateTimeOffset a = Now.AddDays(-2);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, a.AddDays(-95), 30, IdeaStatus.Stop, -1m),
            .. Closed(Linear, a.AddDays(-89), 30, IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m)
        ];

        CalibrationReport report = Report(outcomes, 90, null);

        (report.Checked, report.Unchecked, report.Brier).ShouldBe((1, 0, 0m));
    }

    // The window moves with the checked cases: the 30 targets X saw have left it when Y fires 88 days later, so Y sees only X.
    [Test]
    public void Cases_leave_the_window_as_the_checked_cases_go_on()
    {
        DateTimeOffset x = Now.AddDays(-89);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, x.AddDays(-80), 30, IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, x, x.AddHours(1), IdeaStatus.Expired, 0.1m),
            new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)
        ];

        CalibrationReport report = Report(outcomes, 90, null);

        (report.Checked, report.Unchecked).ShouldBe((1, 1));
    }

    // Scenario "Корзина без статистики".
    [Test]
    public void Case_of_a_bucket_with_fewer_than_30_cases_before_it_is_not_checked()
    {
        IdeaOutcome[] outcomes = [.. Closed(Linear, Now.AddDays(-100), 20, IdeaStatus.Target, 2m), new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)];

        CalibrationReport report = Report(outcomes, 90, null);

        (report.Checked, report.Unchecked, report.Brier, report.BaseBrier).ShouldBe((0, 1, null, null));
    }

    // Scenario "Оценка Брайера": both promised 40 %, one reached its target, the other expired: (0.36 + 0.16) / 2.
    [Test]
    public void Brier_score_is_the_mean_square_of_the_promise_minus_the_outcome()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, Now.AddDays(-50), 12, IdeaStatus.Target, 2m),
            .. Closed(Linear, Now.AddDays(-49), 18, IdeaStatus.Stop, -1m),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Expired, 0.1m)
        ];

        Report(outcomes, 90, null).Brier.ShouldBe(0.26m);
    }

    // The score without buckets promises each case the share of targets of its category: linear has 30 targets of 60, spot 10 of 30.
    [Test]
    public void Score_without_buckets_promises_the_share_of_targets_of_the_category()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, Now.AddDays(-50), 30, IdeaStatus.Target, 2m),
            .. Closed(OtherLinear, Now.AddDays(-50), 30, IdeaStatus.Stop, -1m),
            .. Closed(Spot, Now.AddDays(-50), 10, IdeaStatus.Target, 2m),
            .. Closed(Spot, Now.AddDays(-49), 20, IdeaStatus.Stop, -1m),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(OtherLinear, a, a.AddHours(1), IdeaStatus.Stop, -1m),
            new IdeaOutcome(Spot, a, a.AddHours(1), IdeaStatus.Target, 2m)
        ];

        // Linear: promised 1 and 0, both right; without buckets 0.5 each.
        (Report(outcomes, 90, MarketCategory.Linear).Brier, Report(outcomes, 90, MarketCategory.Linear).BaseBrier).ShouldBe((0m, 0.25m));
        // Spot: 1 / 3 either way, and the target came: (2 / 3)².
        Report(outcomes, 90, MarketCategory.Spot).Brier!.Value.ShouldBe(4m / 9m, 0.0000001m);
        Report(outcomes, 90, MarketCategory.Spot).BaseBrier!.Value.ShouldBe(4m / 9m, 0.0000001m);
        // All: (0 + 0 + 4 / 9) / 3 against (0.25 + 0.25 + 4 / 9) / 3.
        Report(outcomes, 90, null).Brier!.Value.ShouldBe(4m / 27m, 0.0000001m);
        Report(outcomes, 90, null).BaseBrier!.Value.ShouldBe(0.3148148m, 0.0000001m);
    }

    // Two windows × three categories; a case fired 60 days ago counts in the 90 days only. The history 95 days back is checked by nothing.
    [Test]
    public void Reports_cover_90_and_30_days_for_all_linear_and_spot()
    {
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, Now.AddDays(-95), 30, IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, Now.AddDays(-60), Now.AddDays(-60).AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, Now.AddDays(-10), Now.AddDays(-10).AddHours(1), IdeaStatus.Stop, -1m)
        ];

        IReadOnlyList<CalibrationReport> reports = CalibrationCalculator.Compute(outcomes, Now);

        reports.Select(report => (report.Days, report.Category, report.Checked)).ShouldBe(
        [
            (90, null, 2),
            (90, MarketCategory.Linear, 2),
            (90, MarketCategory.Spot, 0),
            (30, null, 1),
            (30, MarketCategory.Linear, 1),
            (30, MarketCategory.Spot, 0)
        ],
            ignoreOrder: true);
        reports.ShouldAllBe(report => report.ComputedAt == Now);
    }

    // Scenarios "Высокие вероятности завышены" and "Значок на деле" on a small set: 30 cases promised 20 % and +0.2 R exactly, which go into
    // the rows from 20 % and from 0.2 R, and a case promised 1 / 6 and −0.5 R.
    [Test]
    public void Tables_group_the_checked_cases_by_promised_probability_expectancy_and_badge()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Closed(Linear, Now.AddDays(-50), 6, IdeaStatus.Target, 2m),
            .. Closed(Linear, Now.AddDays(-49), 24, IdeaStatus.Stop, -0.25m),
            .. Closed(OtherLinear, Now.AddDays(-50), 5, IdeaStatus.Target, 2m),
            .. Closed(OtherLinear, Now.AddDays(-49), 25, IdeaStatus.Stop, -1m),
            .. Closed(Linear, a, 6, IdeaStatus.Target, 2m),
            .. Closed(Linear, a, 21, IdeaStatus.Stop, -0.25m),
            .. Closed(Linear, a, 3, IdeaStatus.Expired, 0m),
            new IdeaOutcome(OtherLinear, a, a.AddHours(1), IdeaStatus.Stop, -1m)
        ];

        CalibrationReport report = Report(outcomes, 90, null);

        // The 30 cases of the bucket: 6 targets, 21 stops, 3 expired; ΣR = 12 − 5.25, ΣR² = 24 + 1.3125.
        BucketStatistics thirty = BucketStatisticsCalculator.Compute(6, 21, 3, 6.75m, 25.3125m);
        BucketStatistics single = BucketStatisticsCalculator.Compute(0, 1, 0, -1m, 1m);
        report.Probabilities.ShouldBe(
        [
            new CalibrationRow(null, 0.2m, null, 5m / 30m, single),
            new CalibrationRow(0.2m, 0.3m, null, 0.2m, thirty)
        ]);
        (report.Probabilities[1].TargetShare, report.Probabilities[1].StopShare, report.Probabilities[1].ExpiredShare).ShouldBe((0.2m, 0.7m, 0.1m));
        report.Expectancies.ShouldBe(
        [
            new CalibrationRow(null, -0.2m, null, -0.5m, single),
            new CalibrationRow(0.2m, 0.4m, null, 0.2m, thirty)
        ]);
        report.Badges.ShouldBe(
        [
            new CalibrationRow(null, null, IdeaBadge.Flat, 0.2m, thirty),
            new CalibrationRow(null, null, IdeaBadge.Down, -0.5m, single)
        ]);
        report.Total.ShouldBe(new CalibrationRow(null, null, null, ((30 * 0.2m) - 0.5m) / 31, BucketStatisticsCalculator.Compute(6, 22, 3, 5.75m, 26.3125m)));
    }

    [Test]
    public void High_promises_go_to_the_open_rows_at_the_top()
    {
        IdeaOutcome[] outcomes = [.. Closed(Linear, Now.AddDays(-50), 30, IdeaStatus.Target, 2m), new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)];

        CalibrationReport report = Report(outcomes, 90, null);

        report.Probabilities.Select(row => (row.From, row.To, row.Promised)).ShouldBe([(0.6m, (decimal?)null, 1m)]);
        report.Expectancies.Select(row => (row.From, row.To, row.Promised)).ShouldBe([(0.4m, (decimal?)null, 2m)]);
    }

    [Test]
    public void Report_without_checked_cases_has_empty_tables()
    {
        CalibrationReport report = Report([], 30, MarketCategory.Spot);

        report.Probabilities.ShouldBeEmpty();
        report.Expectancies.ShouldBeEmpty();
        report.Badges.ShouldBeEmpty();
        report.Total.ShouldBeNull();
    }

    private static CalibrationReport Report(IReadOnlyList<IdeaOutcome> outcomes, int days, MarketCategory? category)
    {
        return CalibrationCalculator.Compute(outcomes, Now).Single(report => report.Days == days && report.Category == category);
    }

    /// <summary><paramref name="count" /> cases of a bucket fired a minute apart from <paramref name="from" />, each closed half an hour later.</summary>
    private static IEnumerable<IdeaOutcome> Closed(IdeaBucketKey key, DateTimeOffset from, int count, IdeaStatus status, decimal result)
    {
        return Enumerable.Range(0, count).Select(index => new IdeaOutcome(key, from.AddMinutes(index), from.AddMinutes(index + 30), status, result));
    }
}