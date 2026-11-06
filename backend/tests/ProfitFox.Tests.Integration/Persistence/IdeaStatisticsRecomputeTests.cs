using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Decision 7: the scanner recomputes the buckets from the stored signals of the version, over 90 and the last 30 days, by the exit rules
///     applied, with their outcomes and the interval of the expectancy. The signals of one key fire an hour apart, each closed before the next.
/// </summary>
public class IdeaStatisticsRecomputeTests
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
    public async Task Buckets_of_the_stored_signals_are_written_for_90_and_30_days()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        IdeaSignal[] old = [.. Enumerable.Range(0, 20).Select(index => IdeaSignalGenerator.Generate(Now.AddDays(-60).AddHours(index), index < 8 ? IdeaStatus.Target : IdeaStatus.Stop))];
        IdeaSignal[] recent = [.. Enumerable.Range(0, 20).Select(index => IdeaSignalGenerator.Generate(Now.AddDays(-10).AddHours(index), index < 10 ? IdeaStatus.Target : IdeaStatus.Stop))];
        IdeaSignal target = IdeaSignalGenerator.Generate(Now.AddDays(-5));
        await new IdeaSignalRepository(_dataSource).UpsertAsync([.. old, .. recent, target], CancellationToken.None);

        await Statistics().RecomputeAsync(CancellationToken.None);

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
        (await new IdeaCalibrationRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, 30, null, CancellationToken.None))!.Checked.ShouldBe(11);
    }

    // Scenario "Смена срока без прогона": a lifetime of one candle closes the targets of 30 minutes as expired at the close of 100.
    [Test]
    public async Task Applied_rule_changes_the_outcomes_without_a_replay()
    {
        await new IdeaSignalRepository(_dataSource).UpsertAsync([IdeaSignalGenerator.Generate(Now.AddDays(-5))], CancellationToken.None);
        await new IdeaExitRuleRepository(_dataSource).SaveAsync(new IdeaExitRule(IdeaType.TwoR, Interval.FifteenMinutes, 1, 2m, null, Now), CancellationToken.None);

        await Statistics().RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        (await new IdeaBucketRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, CancellationToken.None))
            .ShouldAllBe(bucket => bucket.Statistics.Expired == 1 && bucket.Statistics.Targets == 0);
    }

    private IdeaStatistics Statistics()
    {
        return new IdeaStatistics(
            new IdeaSignalRepository(_dataSource),
            new IdeaExitRuleRepository(_dataSource),
            new IdeaBucketRepository(_dataSource),
            new IdeaCalibrationRepository(_dataSource),
            new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings() } },
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new FakeTimeProvider(Now));
    }
}