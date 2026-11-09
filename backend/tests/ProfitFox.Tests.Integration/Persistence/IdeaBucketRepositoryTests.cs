using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Decision 7 of add-own-idea-numbers: a bucket keeps its expectancy without costs and the interval of it; a bucket written without one, as
///     before the first recompute after the change, is read without one.
/// </summary>
public class IdeaBucketRepositoryTests
{
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Bucket_keeps_its_expectancy_without_costs_and_one_without_it_is_read_without_it()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.OneHour, IdeaSide.Long, MarketCategory.Linear);
        var statistics = new BucketStatistics(400, 180, 200, 20, 0.45m, 0.4m, 0.5m, -0.21m, -0.25m, -0.17m) { OwnExpectancy = -0.2m };
        IdeaBucket[] written =
        [
            new IdeaBucket(key, 90, statistics with { GrossExpectancy = -0.02m, GrossExpectancyLow = -0.06m, GrossExpectancyHigh = 0.02m }),
            new IdeaBucket(key, 30, statistics)
        ];

        await buckets.ReplaceAsync(2, written, [], null, CancellationToken.None);

        (await buckets.GetAsync(2, CancellationToken.None)).ShouldBe(written, ignoreOrder: true);
    }

    // Decision 7: the whole recompute replaces the curves of «Уровни» with the buckets, every band of them, and those of other versions go.
    [Test]
    public async Task Whole_replace_replaces_the_curves()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        await buckets.ReplaceAsync(1, [], [Curve(Interval.FourHours, 50)], null, CancellationToken.None);
        await buckets.ReplaceAsync(2, [], [Curve(Interval.OneHour, 400), Curve(Interval.FifteenMinutes, 300) with { Side = IdeaSide.Short, Category = MarketCategory.Spot }], null, CancellationToken.None);

        await buckets.ReplaceAsync(2, [], [Curve(Interval.FiveMinutes, 200)], null, CancellationToken.None);

        Flat(await buckets.GetCurvesAsync(2, CancellationToken.None)).ShouldBe(Flat([Curve(Interval.FiveMinutes, 200)]), ignoreOrder: true);
        (await buckets.GetCurvesAsync(1, CancellationToken.None)).ShouldBeEmpty();
    }

    // The recompute of «Уровни» on one interval replaces the curve of that interval only; that of another type leaves the curves as they were.
    [Test]
    public async Task Replace_of_a_type_and_interval_replaces_the_curve_of_levels_only()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        await buckets.ReplaceAsync(2, [], [Curve(Interval.OneHour, 400), Curve(Interval.FifteenMinutes, 300)], null, CancellationToken.None);

        await buckets.ReplaceAsync(2, [], [Curve(Interval.OneHour, 500)], (IdeaType.Levels, Interval.OneHour), CancellationToken.None);
        await buckets.ReplaceAsync(2, [], [], (IdeaType.TwoR, Interval.FifteenMinutes), CancellationToken.None);

        Flat(await buckets.GetCurvesAsync(2, CancellationToken.None)).ShouldBe(Flat([Curve(Interval.OneHour, 500), Curve(Interval.FifteenMinutes, 300)]), ignoreOrder: true);
    }

    private static RiskRewardCurve Curve(Interval interval, int cases)
    {
        return new RiskRewardCurve(
            interval,
            IdeaSide.Long,
            MarketCategory.Linear,
            [
                new RiskRewardBandCount(RiskRewardBand.UpTo2, cases, cases / 4),
                new RiskRewardBandCount(RiskRewardBand.From2To3, cases - 1, cases / 5),
                new RiskRewardBandCount(RiskRewardBand.From3To6, cases - 2, cases / 7),
                new RiskRewardBandCount(RiskRewardBand.From6, cases - 3, 0)
            ]);
    }

    private static IEnumerable<(Interval, IdeaSide, MarketCategory, RiskRewardBand, int, int)> Flat(IEnumerable<RiskRewardCurve> curves)
    {
        return curves.SelectMany(curve => curve.Bands.Select(band => (curve.Interval, curve.Side, curve.Category, band.Band, band.Cases, band.Targets)));
    }
}