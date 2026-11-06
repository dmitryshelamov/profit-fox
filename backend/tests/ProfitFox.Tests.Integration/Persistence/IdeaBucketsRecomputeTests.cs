using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>Task 5.2: the scanner recomputes the buckets from every stored case of the version, over 90 and the last 30 days, with their outcomes and the interval of the expectancy.</summary>
public class IdeaBucketsRecomputeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");

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
    public async Task Buckets_of_the_stored_cases_are_written_for_90_and_30_days()
    {
        var cases = new IdeaCaseRepository(_dataSource);
        var buckets = new IdeaBucketRepository(_dataSource);
        IdeaCase[] old = [.. Enumerable.Range(0, 20).Select(index => Case(Now.AddDays(-60).AddMinutes(index), index < 8 ? IdeaStatus.Target : IdeaStatus.Stop))];
        IdeaCase[] recent = [.. Enumerable.Range(0, 20).Select(index => Case(Now.AddDays(-10).AddMinutes(index), index < 10 ? IdeaStatus.Target : IdeaStatus.Stop))];
        IdeaCase target = Case(Now.AddDays(-5), IdeaStatus.Target);
        IdeaCase weak = target with { Idea = target.Idea with { Move = 2.5m } };
        await cases.AddAsync([.. old, .. recent, weak], CancellationToken.None);
        var replay = new IdeasReplay(
            new CandleRepository(_dataSource),
            cases,
            new IdeaReplayRepository(_dataSource),
            new IdeaVolatilityRepository(_dataSource),
            buckets,
            new IdeaCalibration(cases, new IdeaCalibrationRepository(_dataSource), new IdeasEngineVersion(IdeasEngineVersion.Current)),
            new ScannerMarket(),
            new IdeasSettings(),
            new BacktestSettings(),
            new ScannerSettings(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new ScannerPace(),
            new FakeTimeProvider(Now));

        await replay.RecomputeBucketsAsync(CancellationToken.None);

        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        IReadOnlyList<IdeaBucket> read = await buckets.GetAsync(IdeasEngineVersion.Current, CancellationToken.None);
        read.ShouldBe(
        [
            new IdeaBucket(key, 90, BucketStatisticsCalculator.Compute(19, 22, 0, (19 * 2m) - 22m, (19 * 4m) + 22m)),
            new IdeaBucket(key, 30, BucketStatisticsCalculator.Compute(11, 10, 0, (11 * 2m) - 10m, (11 * 4m) + 10m))
        ],
            ignoreOrder: true);
        BucketStatistics full = read.Single(bucket => bucket.Days == 90).Statistics;
        (full.Targets, full.Stops, full.Badge).ShouldBe((19, 22, IdeaBadge.Flat));
        (full.ExpectancyLow!.Value < 0m, full.ExpectancyHigh!.Value > 0m).ShouldBe((true, true));
    }

    private static IdeaCase Case(DateTimeOffset firedAt, IdeaStatus status)
    {
        Idea idea = IdeaGenerator.Generate(firedAt, status, statusAt: firedAt.AddHours(1));

        return new IdeaCase(idea, status == IdeaStatus.Target ? 2m : -1m);
    }
}