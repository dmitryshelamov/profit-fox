using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Калибровка на будущих исходах", "Оценка Брайера калибровки", "Таблицы калибровки", "Проверка после изменения правил" and
///     "Сравнение старой и новой модели"; decision 5 of honest-idea-intervals: each case is checked by the model of its bucket as it was when it fired.
/// </summary>
public class CalibrationCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly IdeaBucketKey Linear = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
    private static readonly IdeaBucketKey OtherLinear = Linear with { Setup = IdeaSetup.Drop };
    private static readonly IdeaBucketKey Spot = Linear with { Category = MarketCategory.Spot };

    // Scenario "Закрытый позже случай не подсказывает": B fired before A and closed after it; the bucket of A has 29 cases without B, so A is
    // checked with «мало своих данных», as B is.
    [Test]
    public void Case_closed_after_the_checked_one_fired_is_not_in_the_window_of_its_bucket()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Mixed(OtherLinear, Now.AddDays(-80), 30),
            .. Mixed(Linear, Now.AddDays(-70), 29),
            new IdeaOutcome(Linear, a.AddHours(-1), a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(2), IdeaStatus.Expired, 0.1m)
        ];

        CalibrationReport report = Report(outcomes, 30, null);

        (report.Checked, report.Unchecked).ShouldBe((2, 0));
        report.Owns.Select(row => (row.FewOwnCases, row.Row.Outcome.Cases)).ShouldBe([(true, 2)]);
    }

    // Scenario "Закрытый позже случай не подсказывает" in the window of the parent: without B of the other bucket the parent of A has 29 cases.
    [Test]
    public void Case_closed_after_the_checked_one_fired_is_not_in_the_window_of_its_parent()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Mixed(OtherLinear, Now.AddDays(-80), 15),
            .. Mixed(Linear, Now.AddDays(-70), 14),
            new IdeaOutcome(OtherLinear, a.AddHours(-1), a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(2), IdeaStatus.Expired, 0.1m)
        ];

        CalibrationReport report = Report(outcomes, 30, null);

        (report.Checked, report.Unchecked).ShouldBe((0, 2));
    }

    // Scenario "Старый случай вне окна": 30 cases closed 95 days before A tell it nothing, 89 days before they do.
    [TestCase(95, 0)]
    [TestCase(89, 1)]
    public void Case_fired_more_than_90_days_before_the_checked_one_is_not_in_its_window(int daysBefore, int checkedCases)
    {
        DateTimeOffset a = Now.AddDays(-2);
        IdeaOutcome[] outcomes = [.. Mixed(Linear, a.AddDays(-daysBefore), 30), new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m)];

        Report(outcomes, 30, null).Checked.ShouldBe(checkedCases);
    }

    // A case fired more than 90 days before the checked one never was in its window, however it closed: one closed after A fired leaves
    // without having entered, one that closed 95 days after it fired left before it closed. A sees the 30 cases of its parent, or 29.
    [TestCase(30, 1, 0)]
    [TestCase(29, -5, 1)]
    public void Case_that_left_the_window_before_it_closed_is_never_in_it(int history, int closedDaysAfterA, int uncheckedCases)
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Mixed(Linear, a.AddDays(-50), history),
            new IdeaOutcome(OtherLinear, a.AddDays(-100), a.AddDays(closedDaysAfterA).AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(2), IdeaStatus.Expired, 0.1m)
        ];

        Report(outcomes, 30, null).Unchecked.ShouldBe(uncheckedCases);
    }

    // Scenario "Корзина без статистики": a parent of 20 cases before the case.
    [Test]
    public void Case_of_a_parent_with_fewer_than_30_cases_before_it_is_not_checked()
    {
        IdeaOutcome[] outcomes = [.. Mixed(Linear, Now.AddDays(-100), 10), .. Mixed(OtherLinear, Now.AddDays(-50), 10), new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)];

        CalibrationReport report = Report(outcomes, 30, null);

        (report.Checked, report.Unchecked, report.Brier, report.BaseBrier).ShouldBe((0, 1, null, null));
    }

    // Scenario "Корзина с малым числом своих случаев проверяется": 20 cases of the bucket, 120 of the parent.
    [Test]
    public void Case_of_a_bucket_with_few_own_cases_is_checked_by_the_model()
    {
        IdeaOutcome[] outcomes = [.. Mixed(Linear, Now.AddDays(-80), 20), .. Mixed(OtherLinear, Now.AddDays(-75), 100), new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)];

        CalibrationReport report = Report(outcomes, 30, null);

        report.Checked.ShouldBe(1);
        report.Owns.Single().FewOwnCases.ShouldBeTrue();
    }

    // Scenario "Сила подтягивания на начало суток": C closed at 09:00 of the day A fired at 15:00. C is in the windows of A, but the spread
    // between the buckets is that of 00:00, when the other bucket had 29 cases and no spread to tell: A is promised the model of its parent.
    [Test]
    public void Spread_between_the_buckets_is_that_of_the_start_of_the_day()
    {
        DateTimeOffset day = Now.AddDays(-2);
        var c = new IdeaOutcome(OtherLinear, Now.AddDays(-40), day.AddHours(9), IdeaStatus.Target, 2m);
        var a = new IdeaOutcome(Linear, day.AddHours(15), day.AddHours(16), IdeaStatus.Stop, -1m);
        IdeaOutcome[] history = [.. Mixed(Linear, Now.AddDays(-50), 30), .. Enumerable.Range(0, 29).Select(index => Closed(OtherLinear, Now.AddDays(-45).AddHours(index), IdeaStatus.Target, 2m))];

        CalibrationReport report = Report([.. history, c, a], 30, null);

        ClusteredSums bucket = Sums(history.Where(outcome => outcome.Key == Linear));
        ClusteredSums parent = Sums([.. history, c]);
        BucketModel.ParentModel model = BucketModel.Model(parent, new BucketModel.Root(BucketModel.Category(parent)!, default))!;
        decimal atTheStart = BucketModel.Statistics(bucket, model, default).Probability!.Value;
        decimal atTheCheck = BucketModel.Statistics(bucket, model, BucketModel.Between([bucket, Sums([.. history.Where(outcome => outcome.Key == OtherLinear), c])], model.Own))
            .Probability!.Value;
        atTheCheck.ShouldNotBe(atTheStart);
        report.Probabilities.Single().Promised.ShouldBe(atTheStart);
    }

    // Scenario "Оценка Брайера": one bucket of one parent is its category: both are promised its 12 targets of 30; one reached its target,
    // the other expired: (0.36 + 0.16) / 2.
    [Test]
    public void Brier_score_is_the_mean_square_of_the_promise_minus_the_outcome()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Enumerable.Range(0, 12).Select(index => Closed(Linear, Now.AddDays(-50).AddHours(index), IdeaStatus.Target, 2m)),
            .. Enumerable.Range(0, 18).Select(index => Closed(Linear, Now.AddDays(-49).AddHours(index), IdeaStatus.Stop, -1m)),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Expired, 0.1m)
        ];

        Report(outcomes, 90, null).Brier.ShouldBe(0.26m);
    }

    // The score without buckets promises each case the share of targets of its category: linear has 20 targets of 60, spot 10 of 30.
    [Test]
    public void Score_without_buckets_promises_the_share_of_targets_of_the_category()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] outcomes =
        [
            .. Mixed(Linear, Now.AddDays(-50), 30),
            .. Mixed(OtherLinear, Now.AddDays(-48), 30),
            .. Mixed(Spot, Now.AddDays(-50), 30),
            new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m),
            new IdeaOutcome(OtherLinear, a, a.AddHours(1), IdeaStatus.Stop, -1m),
            new IdeaOutcome(Spot, a, a.AddHours(1), IdeaStatus.Target, 2m)
        ];

        // Linear: 1 / 3 promised to a target and to a stop, (4 / 9 + 1 / 9) / 2; spot: (2 / 3)².
        Report(outcomes, 30, MarketCategory.Linear).BaseBrier!.Value.ShouldBe(5m / 18m, 0.0000001m);
        Report(outcomes, 30, MarketCategory.Spot).BaseBrier!.Value.ShouldBe(4m / 9m, 0.0000001m);
        Report(outcomes, 30, null).BaseBrier!.Value.ShouldBe(((5m / 9m) + (4m / 9m)) / 3m, 0.0000001m);
    }

    // The window moves with the checked cases: the 30 cases X saw have left it when Y fires 88 days later, so Y sees only X.
    [Test]
    public void Cases_leave_the_window_as_the_checked_cases_go_on()
    {
        DateTimeOffset x = Now.AddDays(-89);
        IdeaOutcome[] outcomes =
        [
            .. Mixed(Linear, x.AddDays(-80), 30),
            new IdeaOutcome(Linear, x, x.AddHours(1), IdeaStatus.Expired, 0.1m),
            new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)
        ];

        CalibrationReport report = Report(outcomes, 90, null);

        (report.Checked, report.Unchecked).ShouldBe((1, 1));
    }

    // Scenario "Строка «мало своих данных»": the bucket has no case of its own, its parent 12 targets of 30 of the other bucket; both cases are
    // promised the numbers of the parent, 40 % and +0.2 R, and fired in one hour: (0.36 + 0.16) / 2.
    [Test]
    public void Row_of_few_own_cases_has_the_promise_the_outcome_and_the_brier_score()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] checkedCases = [new IdeaOutcome(Linear, a, a.AddHours(1), IdeaStatus.Target, 2m), new IdeaOutcome(Linear, a.AddMinutes(5), a.AddHours(1), IdeaStatus.Expired, 0.1m)];
        IdeaOutcome[] outcomes = [.. Enumerable.Range(0, 30).Select(index => Closed(OtherLinear, Now.AddDays(-50).AddHours(index), index < 12 ? IdeaStatus.Target : IdeaStatus.Stop, index < 12 ? 2m : -1m)), .. checkedCases];

        CalibrationReport report = Report(outcomes, 30, null);

        report.Owns.ShouldBe([new CalibrationOwnRow(true, new CalibrationRow(null, null, null, 0.2m, Sums(checkedCases).Statistics()), 0.26m)]);
    }

    // Scenario "Значок на деле" with the clustering: the same 40 ▲ cases, half targets and half stops, are one movement when they fired in
    // two hours, and their row is wider than when each fired in an hour of its own.
    [Test]
    public void Row_of_cases_of_one_hour_is_wider()
    {
        double Width(Func<int, DateTimeOffset> firedAt)
        {
            IdeaOutcome[] outcomes =
            [
                .. Enumerable.Range(0, 60).Select(index => Closed(Linear, Now.AddDays(-60).AddHours(index), index < 50 ? IdeaStatus.Target : IdeaStatus.Stop, index < 50 ? 2m : -1m)),
                .. Enumerable.Range(0, 40).Select(index => new IdeaOutcome(Linear, firedAt(index), Now, index < 20 ? IdeaStatus.Target : IdeaStatus.Stop, index < 20 ? 2m : -1m))
            ];
            CalibrationRow up = Report(outcomes, 30, null).Badges.Single(row => row.Badge == IdeaBadge.Up);

            return (double)(up.Outcome.ExpectancyHigh!.Value - up.Outcome.ExpectancyLow!.Value);
        }

        double apart = Width(index => Now.AddDays(-10).AddHours(index));
        double together = Width(index => Now.AddDays(-10).AddHours(index < 20 ? 0 : 1).AddMinutes(index % 20));

        together.ShouldBeGreaterThan(apart * 3);
    }

    // Scenarios "Высокие вероятности завышены" and "Значок на деле" on a small set: one bucket, its parent and its category are the same 30
    // cases, so 30 cases checked together are all promised their 20 % and +0.2 R, which go into the rows from 20 % and from 0.2 R.
    [Test]
    public void Tables_group_the_checked_cases_by_promised_probability_expectancy_and_badge()
    {
        DateTimeOffset a = Now.AddDays(-1);
        IdeaOutcome[] history =
        [
            .. Enumerable.Range(0, 6).Select(index => Closed(Linear, Now.AddDays(-50).AddHours(index), IdeaStatus.Target, 2m)),
            .. Enumerable.Range(0, 24).Select(index => Closed(Linear, Now.AddDays(-49).AddHours(index), IdeaStatus.Stop, -0.25m))
        ];
        IdeaOutcome[] checkedCases =
        [
            .. Enumerable.Range(0, 6).Select(index => new IdeaOutcome(Linear, a.AddHours(index), Now, IdeaStatus.Target, 2m)),
            .. Enumerable.Range(0, 21).Select(index => new IdeaOutcome(Linear, a.AddHours(index), Now, IdeaStatus.Stop, -0.25m)),
            .. Enumerable.Range(0, 3).Select(index => new IdeaOutcome(Linear, a.AddHours(index), Now, IdeaStatus.Expired, 0m))
        ];

        CalibrationReport report = Report([.. history, .. checkedCases], 30, null);

        BucketStatistics outcome = Sums(checkedCases).Statistics();
        report.Probabilities.ShouldBe([new CalibrationRow(0.2m, 0.3m, null, 0.2m, outcome)]);
        (report.Probabilities[0].TargetShare, report.Probabilities[0].StopShare, report.Probabilities[0].ExpiredShare).ShouldBe((0.2m, 0.7m, 0.1m));
        report.Expectancies.ShouldBe([new CalibrationRow(0.2m, 0.4m, null, 0.2m, outcome)]);
        report.Badges.ShouldBe([new CalibrationRow(null, null, Sums(history).Statistics().Badge, 0.2m, outcome)]);
        report.Total.ShouldBe(new CalibrationRow(null, null, null, 0.2m, outcome));
    }

    [Test]
    public void High_promises_go_to_the_open_rows_at_the_top()
    {
        IdeaOutcome[] outcomes =
        [
            .. Enumerable.Range(0, 30).Select(index => Closed(Linear, Now.AddDays(-50).AddHours(index), index < 25 ? IdeaStatus.Target : IdeaStatus.Stop, index < 25 ? 2m : -1m)),
            new IdeaOutcome(Linear, Now.AddDays(-1), Now.AddHours(-1), IdeaStatus.Target, 2m)
        ];

        CalibrationReport report = Report(outcomes, 30, null);

        report.Probabilities.Select(row => (row.From, row.To, row.Promised)).ShouldBe([(0.6m, (decimal?)null, 25m / 30m)]);
        report.Expectancies.Select(row => (row.From, row.To, row.Promised)).ShouldBe([(0.4m, (decimal?)null, 1.5m)]);
    }

    [Test]
    public void Report_without_checked_cases_has_empty_tables()
    {
        CalibrationReport report = Report([], 30, MarketCategory.Spot);

        report.Probabilities.ShouldBeEmpty();
        report.Expectancies.ShouldBeEmpty();
        report.Badges.ShouldBeEmpty();
        report.Owns.ShouldBeEmpty();
        report.Total.ShouldBeNull();
    }

    private static CalibrationReport Report(IReadOnlyList<IdeaOutcome> outcomes, int days, MarketCategory? category, CalibrationScope scope = CalibrationScope.All)
    {
        return CalibrationCalculator.Compute(outcomes, Now, (_, _) => DateTimeOffset.MinValue)
            .Single(report => report.Days == days && report.Category == category && report.Scope == scope);
    }

    /// <summary>A case of a bucket fired at <paramref name="firedAt" /> and closed half an hour later.</summary>
    private static IdeaOutcome Closed(IdeaBucketKey key, DateTimeOffset firedAt, IdeaStatus status, decimal result)
    {
        return new IdeaOutcome(key, firedAt, firedAt.AddMinutes(30), status, result);
    }

    /// <summary><paramref name="count" /> cases of a bucket an hour apart from <paramref name="from" />, every third a +2 R target, the rest −1 R stops.</summary>
    private static IEnumerable<IdeaOutcome> Mixed(IdeaBucketKey key, DateTimeOffset from, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => Closed(key, from.AddHours(index), index % 3 == 0 ? IdeaStatus.Target : IdeaStatus.Stop, index % 3 == 0 ? 2m : -1m));
    }

    private static ClusteredSums Sums(IEnumerable<IdeaOutcome> outcomes)
    {
        var sums = new ClusteredSums();

        foreach (IdeaOutcome outcome in outcomes)
        {
            sums.Add(outcome.FiredAt, outcome.Status, outcome.Result);
        }

        return sums;
    }
}