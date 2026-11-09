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

        await buckets.ReplaceAsync(2, written, null, CancellationToken.None);

        (await buckets.GetAsync(2, CancellationToken.None)).ShouldBe(written, ignoreOrder: true);
    }
}