using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decision 7: the reports of the calibration, replaced whole and read by window and category.</summary>
public class IdeaCalibrationRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:10:00Z");

    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Reports_of_a_version_are_replaced_whole_and_read_by_window_and_category()
    {
        var calibrations = new IdeaCalibrationRepository(_dataSource);
        BucketStatistics thirty = BucketStatisticsCalculator.Compute(6, 21, 3, 6.75m, 25.3125m);
        BucketStatistics single = BucketStatisticsCalculator.Compute(0, 1, 0, -1m, 1m);
        var linear = new CalibrationReport(
            Now,
            30,
            MarketCategory.Linear,
            31,
            4,
            0.207m,
            0.219m,
            [new CalibrationRow(null, 0.2m, null, 5m / 30m, single), new CalibrationRow(0.2m, 0.3m, null, 0.2m, thirty)],
            [new CalibrationRow(0.4m, null, null, 0.45m, thirty)],
            [new CalibrationRow(null, null, IdeaBadge.Flat, 0.2m, thirty)],
            new CalibrationRow(null, null, null, 0.18m, thirty))
        {
            // A report of the comparison without the model with features, as stored before add-idea-features.
            Comparison = new CalibrationComparison(new CalibrationModel(31, 0.22m, null), new CalibrationModel(31, 0.207m, null))
        };
        var all = new CalibrationReport(Now, 90, null, 0, 2, null, null, [], [], [], null);
        // Decision 6 of honest-idea-intervals: the report after the rules changed, with the table by own data and the comparison of the models,
        // the model with features among them (decision 4 of add-idea-features).
        CalibrationReport fresh = linear with
        {
            Scope = CalibrationScope.Fresh,
            Owns = [new CalibrationOwnRow(false, new CalibrationRow(null, null, null, 0.2m, thirty), 0.21m), new CalibrationOwnRow(true, new CalibrationRow(null, null, null, -0.5m, single), 1m)],
            Comparison = new CalibrationComparison(
                new CalibrationModel(31, 0.22m, new CalibrationRow(null, null, IdeaBadge.Up, 0.145m, thirty)),
                new CalibrationModel(31, 0.207m, null),
                new CalibrationModel(31, 0.201m, new CalibrationRow(null, null, IdeaBadge.Up, 0.12m, thirty)),
                new CalibrationDifference(-0.006m, -0.012m, 0.001m)),
            // Requirement "Калибровка по ТФ и типам": the breakdowns by interval and by type.
            Intervals =
            [
                new CalibrationSlice(Interval.FiveMinutes, null, new CalibrationRow(null, null, null, -0.5m, single), null, new CalibrationComparison(new CalibrationModel(0, null, null), new CalibrationModel(0, null, null))),
                new CalibrationSlice(
                    Interval.OneHour,
                    null,
                    new CalibrationRow(null, null, null, 0.2m, thirty),
                    new CalibrationRow(null, null, IdeaBadge.Up, 0.15m, thirty),
                    new CalibrationComparison(new CalibrationModel(30, 0.22m, new CalibrationRow(null, null, IdeaBadge.Up, 0.145m, thirty)), new CalibrationModel(30, 0.2m, null)))
            ],
            Types = [new CalibrationSlice(null, IdeaType.Pullback, new CalibrationRow(null, null, null, 0.2m, thirty), null, new CalibrationComparison(new CalibrationModel(1, 1m, null), new CalibrationModel(1, 0.5m, null)))]
        };
        await calibrations.ReplaceAsync(1, [all with { Days = 30 }], CancellationToken.None);

        await calibrations.ReplaceAsync(2, [all, linear, fresh], CancellationToken.None);

        ShouldMatch(await calibrations.GetAsync(2, 30, MarketCategory.Linear, CalibrationScope.All, CancellationToken.None), linear);
        ShouldMatch(await calibrations.GetAsync(2, 30, MarketCategory.Linear, CalibrationScope.Fresh, CancellationToken.None), fresh);
        ShouldMatch(await calibrations.GetAsync(2, 90, null, CalibrationScope.All, CancellationToken.None), all);
        (await calibrations.GetAsync(2, 90, null, CalibrationScope.Fresh, CancellationToken.None)).ShouldBeNull();
        (await calibrations.GetAsync(2, 90, MarketCategory.Spot, CalibrationScope.All, CancellationToken.None)).ShouldBeNull();
        (await calibrations.GetAsync(1, 30, null, CalibrationScope.All, CancellationToken.None)).ShouldBeNull();
    }

    // Decision 4 of add-idea-features: a report stored before it has neither the model with features nor the difference in its comparison.
    [Test]
    public async Task Report_stored_before_the_model_with_features_reads_without_it()
    {
        const string report = """
                              {"Checked": 0, "Unchecked": 0, "Brier": null, "BaseBrier": null, "Probabilities": [], "Expectancies": [], "Badges": [],
                               "Total": null, "Owns": [], "Comparison": {"Old": {"Cases": 0, "Brier": null, "Up": null}, "New": {"Cases": 0, "Brier": null, "Up": null}}}
                              """;
        await using (NpgsqlCommand insert = _dataSource.CreateCommand(
                         "INSERT INTO idea_calibration (engine_version, days, category, scope, computed_at, report) VALUES (2, 90, 'all', 'all', @Now, @Report::jsonb)"))
        {
            insert.Parameters.AddWithValue("Now", Now);
            insert.Parameters.AddWithValue("Report", report);
            await insert.ExecuteNonQueryAsync();
        }

        CalibrationReport? read = await new IdeaCalibrationRepository(_dataSource).GetAsync(2, 90, null, CalibrationScope.All, CancellationToken.None);

        read!.Comparison.ShouldBe(new CalibrationComparison(new CalibrationModel(0, null, null), new CalibrationModel(0, null, null)));
    }

    // Scenario "Итог, сохранённый до разбивок": a report stored without the breakdowns reads with empty ones.
    [Test]
    public async Task Report_stored_before_the_breakdowns_reads_with_empty_ones()
    {
        var calibrations = new IdeaCalibrationRepository(_dataSource);
        BucketStatistics single = BucketStatisticsCalculator.Compute(0, 1, 0, -1m, 1m);
        var report = new CalibrationReport(Now, 90, null, 1, 0, 1m, 1m, [], [], [], new CalibrationRow(null, null, null, 0.1m, single))
        {
            Types = [new CalibrationSlice(null, IdeaType.TwoR, new CalibrationRow(null, null, null, 0.1m, single), null, new CalibrationComparison(new CalibrationModel(0, null, null), new CalibrationModel(0, null, null)))]
        };
        await calibrations.ReplaceAsync(1, [report], CancellationToken.None);
        await using (NpgsqlCommand strip = _dataSource.CreateCommand("UPDATE idea_calibration SET report = report - 'Intervals' - 'Types'"))
        {
            await strip.ExecuteNonQueryAsync();
        }

        CalibrationReport? read = await calibrations.GetAsync(1, 90, null, CalibrationScope.All, CancellationToken.None);

        read.ShouldNotBeNull();
        read.Intervals.ShouldBeEmpty();
        read.Types.ShouldBeEmpty();
        read.Total.ShouldBe(report.Total);
    }

    // A code of a breakdown that this version does not know is a broken report, not a row of another interval or type.
    [TestCase("Intervals", "{\"Interval\": \"7\"}")]
    [TestCase("Types", "{\"Type\": \"fishing\"}")]
    public async Task Report_with_an_unknown_code_of_a_breakdown_is_refused(string breakdown, string code)
    {
        var calibrations = new IdeaCalibrationRepository(_dataSource);
        BucketStatistics single = BucketStatisticsCalculator.Compute(0, 1, 0, -1m, 1m);
        var slice = new CalibrationSlice(Interval.FiveMinutes, IdeaType.TwoR, new CalibrationRow(null, null, null, 0.1m, single), null, new CalibrationComparison(new CalibrationModel(0, null, null), new CalibrationModel(0, null, null)));
        await calibrations.ReplaceAsync(1, [new CalibrationReport(Now, 90, null, 1, 0, 1m, 1m, [], [], [], null) { Intervals = [slice], Types = [slice] }], CancellationToken.None);
        await using (NpgsqlCommand corrupt = _dataSource.CreateCommand($"UPDATE idea_calibration SET report = jsonb_set(report, '{{{breakdown},0}}', report #> '{{{breakdown},0}}' || '{code}'::jsonb)"))
        {
            await corrupt.ExecuteNonQueryAsync();
        }

        await Should.ThrowAsync<InvalidOperationException>(() => calibrations.GetAsync(1, 90, null, CalibrationScope.All, CancellationToken.None));
    }

    // A record compares its lists by reference: the lists are compared by their rows, the rest as it is.
    private static void ShouldMatch(CalibrationReport? read, CalibrationReport expected)
    {
        read.ShouldNotBeNull();
        read.Probabilities.ShouldBe(expected.Probabilities);
        read.Expectancies.ShouldBe(expected.Expectancies);
        read.Badges.ShouldBe(expected.Badges);
        read.Owns.ShouldBe(expected.Owns);
        read.Intervals.ShouldBe(expected.Intervals);
        read.Types.ShouldBe(expected.Types);
        (read with
        {
            Probabilities = expected.Probabilities,
            Expectancies = expected.Expectancies,
            Badges = expected.Badges,
            Owns = expected.Owns,
            Intervals = expected.Intervals,
            Types = expected.Types
        }).ShouldBe(expected);
    }
}