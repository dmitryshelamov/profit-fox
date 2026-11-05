using Npgsql;

using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Decisions 5 to 7: the cases of the replay, the buckets, the cursors of the replay and the percentiles of volatility.</summary>
public class IdeaCaseRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:05:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private IdeaCaseRepository _cases = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _cases = new IdeaCaseRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    // Test of task 3.2: "повторная запись случая не создаёт дубля"; the case first written stays.
    [Test]
    public async Task Case_written_again_is_no_duplicate()
    {
        IdeaCase first = Case(Now, IdeaStatus.Target, 1.94m);
        await _cases.AddAsync([first], CancellationToken.None);

        await _cases.AddAsync([first with { Result = 5m }, Case(Now.AddMinutes(1), IdeaStatus.Stop, -1.05m)], CancellationToken.None);

        IReadOnlyList<IdeaCase> stored = await _cases.GetAsync(IdeaGenerator.Wif, 2, CancellationToken.None);
        stored.Select(item => (item.Idea.Track.FiredAt, item.Result)).ShouldBe([(Now, 1.94m), (Now.AddMinutes(1), -1.05m)]);
        stored[0].Idea.ShouldBe(first.Idea with { Id = Guid.Empty });
    }

    // Decision 7: the replay runs by the thresholds of the live ideas, so a bucket counts every case of the version, whatever its move and
    // volume; a case just above the threshold counts like any other.
    [Test]
    public async Task Buckets_count_every_case_of_the_version_since_a_time()
    {
        await _cases.AddAsync(
        [
            Case(Now, IdeaStatus.Target, 2m),
            Case(Now.AddMinutes(1), IdeaStatus.Stop, -1m),
            Case(Now.AddMinutes(2), IdeaStatus.Expired, 0.5m),
            Case(Now.AddMinutes(3), IdeaStatus.Target, 2m, move: 3.0001m),
            Case(Now.AddMinutes(4), IdeaStatus.Target, 2m, volumeRatio: 1.8m),
            Case(Now.AddMinutes(5), IdeaStatus.Stop, -1m, setup: IdeaSetup.EarlyImpulse, move: 1.6m, volumeRatio: 3m),
            Case(Now.AddMinutes(6), IdeaStatus.Stop, -1m, setup: IdeaSetup.EarlyImpulse, move: 1.4m, volumeRatio: 3m),
            Case(Now.AddMinutes(7), IdeaStatus.Target, 2m, setup: IdeaSetup.HighVolatility, move: 0.1m, volumeRatio: 0m),
            Case(Now.AddMinutes(8), IdeaStatus.Target, 2m, engineVersion: 3),
            Case(Now.AddDays(-91), IdeaStatus.Target, 2m)
        ],
            CancellationToken.None);

        IReadOnlyList<IdeaBucketCounts> counts = await _cases.CountAsync(2, Now.AddDays(-90), CancellationToken.None);

        counts.ShouldBe(
        [
            new IdeaBucketCounts(Key(IdeaSetup.Impulse), 3, 1, 1, 2m, 5.5m),
            new IdeaBucketCounts(Key(IdeaSetup.EarlyImpulse), 0, 2, 0, 2m, -2m),
            new IdeaBucketCounts(Key(IdeaSetup.HighVolatility), 1, 0, 0, 2m, 2m)
        ],
            ignoreOrder: true);
    }

    // A guard: the mean R:R of long fractions reads as System.Decimal, so it needs no rounding, unlike the total of results.
    [Test]
    public async Task Mean_risk_reward_of_long_fractions_is_read()
    {
        IdeaCase[] cases =
        [
            .. new[] { 98.9m, 98.9m, 98.8m }.Select((stop, index) => Case(Now.AddMinutes(index), IdeaStatus.Target, 2m) is var item
                ? item with { Idea = item.Idea with { Track = item.Idea.Track with { Plan = new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, stop, 110m) } } }
                : null!)
        ];
        await _cases.AddAsync(cases, CancellationToken.None);

        IdeaBucketCounts counts = (await _cases.CountAsync(2, Now.AddDays(-90), CancellationToken.None)).Single();

        // 10 / 1.1 and 10 / 1.2 have 28 digits after the point; their mean past 8 still reads as System.Decimal.
        counts.AverageRiskReward.ShouldBe(((10m / 1.1m * 2) + (10m / 1.2m)) / 3m, 0.000000000001m);
    }

    // A result in R is often a repeating fraction of 28 digits; a total of several of them past 8 holds more digits than System.Decimal.
    [Test]
    public async Task Total_of_long_fractions_is_read_rounded()
    {
        IdeaCase[] cases = [.. Enumerable.Range(0, 3).Select(index => Case(Now.AddMinutes(index), IdeaStatus.Target, 10m / 3m))];
        await _cases.AddAsync(cases, CancellationToken.None);

        IdeaBucketCounts counts = (await _cases.CountAsync(2, Now.AddDays(-90), CancellationToken.None)).Single();

        counts.TotalR.ShouldBe(10m, 0.000000000001m);
    }

    // Decision 6: a new version of the rules clears the cases of the old ones.
    [Test]
    public async Task Cases_of_other_versions_are_removed()
    {
        await _cases.AddAsync([Case(Now, IdeaStatus.Target, 2m), Case(Now, IdeaStatus.Target, 2m, engineVersion: 3)], CancellationToken.None);

        await _cases.DeleteOtherVersionsAsync(3, CancellationToken.None);

        (await _cases.GetAsync(IdeaGenerator.Wif, 2, CancellationToken.None)).ShouldBeEmpty();
        (await _cases.GetAsync(IdeaGenerator.Wif, 3, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Buckets_of_a_version_are_replaced_whole()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        var full = new IdeaBucket(Key(IdeaSetup.Impulse), 90, new BucketStatistics(400, 20, 0.47m, 0.42m, 0.52m, 0.333m, 0.12m));
        var few = new IdeaBucket(Key(IdeaSetup.Drop), 30, new BucketStatistics(12, 1, null, null, null, 0.25m, -0.1m));
        await buckets.ReplaceAsync(1, [new IdeaBucket(Key(IdeaSetup.EarlyDrop), 90, few.Statistics)], CancellationToken.None);

        await buckets.ReplaceAsync(2, [full, few], CancellationToken.None);

        (await buckets.GetAsync(2, CancellationToken.None)).ShouldBe([full, few], ignoreOrder: true);
        (await buckets.GetAsync(1, CancellationToken.None)).ShouldBeEmpty();
    }

    // Fix after review: the cursor keeps the ideas of the replay still open at it, so the next turn goes on from them.
    [Test]
    public async Task Cursors_of_the_replay_are_kept_per_version_with_their_open_ideas()
    {
        var replay = new IdeaReplayRepository(_dataSource);
        var sol = new Instrument(MarketCategory.Spot, "SOLUSDT");
        Idea active = IdeaGenerator.Generate(Now, IdeaStatus.Active, statusAt: Now.AddMinutes(1));
        Idea open = active with { Track = active.Track with { Tick = 0.0001m } };
        await replay.SetAsync(IdeaGenerator.Wif, 1, new IdeaReplayCursor(Now, [open]), CancellationToken.None);
        await replay.SetAsync(IdeaGenerator.Wif, 1, new IdeaReplayCursor(Now.AddDays(1), [open]), CancellationToken.None);
        await replay.SetAsync(sol, 2, new IdeaReplayCursor(Now, []), CancellationToken.None);

        IdeaReplayCursor cursor = (await replay.GetAsync(1, CancellationToken.None)).Single().Value;
        cursor.ReplayedUntil.ShouldBe(Now.AddDays(1));
        cursor.Open.ShouldBe([open]);
        await replay.DeleteOtherVersionsAsync(2, CancellationToken.None);
        (await replay.GetAsync(1, CancellationToken.None)).ShouldBeEmpty();
        (await replay.GetAsync(2, CancellationToken.None)).Keys.ShouldBe([sol]);
    }

    [Test]
    public async Task Percentiles_of_volatility_are_replaced_per_pair_and_interval()
    {
        var volatility = new IdeaVolatilityRepository(_dataSource);
        await volatility.SaveAsync([new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.01m), new IdeaVolatility(IdeaGenerator.Wif, Interval.FiveMinutes, 0.002m)], CancellationToken.None);

        await volatility.SaveAsync([new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.0125m)], CancellationToken.None);

        (await volatility.GetAllAsync(CancellationToken.None)).ShouldBe(
            [new IdeaVolatility(IdeaGenerator.Wif, Interval.OneHour, 0.0125m), new IdeaVolatility(IdeaGenerator.Wif, Interval.FiveMinutes, 0.002m)],
            ignoreOrder: true);
    }

    private static IdeaBucketKey Key(IdeaSetup setup)
    {
        return new IdeaBucketKey(setup, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
    }

    private static IdeaCase Case(
        DateTimeOffset firedAt,
        IdeaStatus status,
        decimal result,
        IdeaSetup setup = IdeaSetup.Impulse,
        decimal move = 3.5m,
        decimal volumeRatio = 2.25m,
        int engineVersion = 2)
    {
        Idea idea = IdeaGenerator.Generate(firedAt, status, setup: setup, statusAt: firedAt.AddMinutes(30), engineVersion: engineVersion);

        return new IdeaCase(idea with { Move = move, VolumeRatio = volumeRatio }, result);
    }
}