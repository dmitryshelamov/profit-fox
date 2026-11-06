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
            new CalibrationRow(null, null, null, 0.18m, thirty));
        var all = new CalibrationReport(Now, 90, null, 0, 2, null, null, [], [], [], null);
        // Decision 6 of honest-idea-intervals: the report after the rules changed, with the table by own data and the comparison of the models.
        CalibrationReport fresh = linear with
        {
            Scope = CalibrationScope.Fresh,
            Owns = [new CalibrationOwnRow(false, new CalibrationRow(null, null, null, 0.2m, thirty), 0.21m), new CalibrationOwnRow(true, new CalibrationRow(null, null, null, -0.5m, single), 1m)],
            Comparison = new CalibrationComparison(
                new CalibrationModel(31, 0.22m, new CalibrationRow(null, null, IdeaBadge.Up, 0.145m, thirty)),
                new CalibrationModel(31, 0.207m, null))
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

    // A record compares its lists by reference: the lists are compared by their rows, the rest as it is.
    private static void ShouldMatch(CalibrationReport? read, CalibrationReport expected)
    {
        read.ShouldNotBeNull();
        read.Probabilities.ShouldBe(expected.Probabilities);
        read.Expectancies.ShouldBe(expected.Expectancies);
        read.Badges.ShouldBe(expected.Badges);
        read.Owns.ShouldBe(expected.Owns);
        (read with { Probabilities = expected.Probabilities, Expectancies = expected.Expectancies, Badges = expected.Badges, Owns = expected.Owns }).ShouldBe(expected);
    }
}