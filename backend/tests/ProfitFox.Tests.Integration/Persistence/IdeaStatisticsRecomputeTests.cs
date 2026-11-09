using Microsoft.Extensions.Time.Testing;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
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
    // The holding of «Цель 2R» on 15 minutes by default, 24 candles: the cases of 6 hours UTC from midnight are one group.
    private static readonly TimeSpan SixHours = TimeSpan.FromHours(6);

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

    // Decision 4 of honest-idea-intervals: the buckets of «Импульс» and of «Пролив», one parent, written with the numbers of the model and their
    // own ones beside; «Пролив» has 12 cases of its own, and over 30 days the parent has fewer than 30, «мало данных».
    [Test]
    public async Task Buckets_of_the_stored_signals_are_written_for_90_and_30_days()
    {
        var buckets = new IdeaBucketRepository(_dataSource);
        (DateTimeOffset FiredAt, IdeaStatus Status, IdeaSetup Setup)[] cases =
        [
            .. Enumerable.Range(0, 20).Select(index => (Now.AddDays(-60).AddHours(index), index < 8 ? IdeaStatus.Target : IdeaStatus.Stop, IdeaSetup.Impulse)),
            .. Enumerable.Range(0, 20).Select(index => (Now.AddDays(-10).AddHours(index), index < 10 ? IdeaStatus.Target : IdeaStatus.Stop, IdeaSetup.Impulse)),
            (Now.AddDays(-5), IdeaStatus.Target, IdeaSetup.Impulse),
            .. Enumerable.Range(0, 12).Select(index => (Now.AddDays(-50).AddHours(index), index < 3 ? IdeaStatus.Target : IdeaStatus.Stop, IdeaSetup.Drop))
        ];
        await new IdeaSignalRepository(_dataSource).UpsertAsync(
            [.. cases.Select(item => IdeaSignalGenerator.Generate(item.FiredAt, item.Status, setup: item.Setup))],
            CancellationToken.None);

        await Statistics().RecomputeAsync(CancellationToken.None);

        IReadOnlyList<IdeaBucket> read = await buckets.GetAsync(IdeasEngineVersion.Current, CancellationToken.None);
        read.ShouldBe([.. Model(cases, 90), .. Model(cases, 30)], ignoreOrder: true);
        BucketStatistics drop = read.Single(bucket => bucket.Days == 90 && bucket.Key.Setup == IdeaSetup.Drop).Statistics;
        (drop.FewOwnCases, drop.OwnExpectancy, drop.ParentWeight).ShouldBe((true, -0.25m, 1m));
        BucketStatistics impulse = read.Single(bucket => bucket.Days == 90 && bucket.Key.Setup == IdeaSetup.Impulse).Statistics;
        (impulse.OwnProbability, impulse.OwnExpectancyLow).ShouldNotBe((null, null));
        // The 21 cases of the last 30 days find the 32 of their parent fired 50 and 60 days ago, and are all checked by the model.
        (await new IdeaCalibrationRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, 30, null, CalibrationScope.All, CancellationToken.None))!.Checked.ShouldBe(21);
    }

    // Task 2.4 of add-own-idea-numbers: the sums without costs are block sums, as those after costs, and keep no outcome of a case. A pass
    // that may keep none still writes the buckets with their expectancy without costs and leaves the calibration as it was.
    [Test]
    public async Task Pass_that_keeps_no_outcome_writes_the_expectancy_without_costs()
    {
        await new IdeaSignalRepository(_dataSource).UpsertAsync(
            [.. Enumerable.Range(0, 40).Select(index => IdeaSignalGenerator.Generate(Now.AddDays(-20).AddHours(index), index % 3 == 0 ? IdeaStatus.Target : IdeaStatus.Stop))],
            CancellationToken.None);

        await Statistics(new ScannerSettings { CalibrationCases = 0 }).RecomputeAsync(CancellationToken.None);

        (await new IdeaBucketRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, CancellationToken.None))
            .ShouldAllBe(bucket => bucket.Statistics.GrossExpectancy == bucket.Statistics.Expectancy && bucket.Statistics.GrossExpectancyLow != null);
        (await new IdeaCalibrationRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, 30, null, CalibrationScope.All, CancellationToken.None)).ShouldBeNull();
    }

    // Decision 3 of cluster-idea-cases-by-holding: the calibration groups by the holding of the rules of the pass. 30 cases 60 days ago give
    // the model; 16 blocks of 6 hours 20 days ago have two cases each, at the first and the fourth hour, a +2 R target in every other block.
    // The 32 cases of the last 30 days are 16 groups of 2: +0.5 R with −0.3255…+1.3255 R by t₁₅, not −0.028…+1.028 R of 32 hours.
    [Test]
    public async Task Calibration_groups_the_cases_by_the_holding_of_the_rules()
    {
        (DateTimeOffset FiredAt, IdeaStatus Status)[] cases =
        [
            .. Enumerable.Range(0, 30).Select(index => (Now.AddDays(-60).AddHours(index), index % 3 == 0 ? IdeaStatus.Target : IdeaStatus.Stop)),
            .. Enumerable.Range(0, 16).SelectMany(block => new[] { 1, 4 }.Select(hour => (Now.AddDays(-20).AddHours((12 * block) + hour), block % 2 == 0 ? IdeaStatus.Target : IdeaStatus.Stop)))
        ];
        await new IdeaSignalRepository(_dataSource).UpsertAsync([.. cases.Select(item => IdeaSignalGenerator.Generate(item.FiredAt, item.Status))], CancellationToken.None);

        await Statistics().RecomputeAsync(CancellationToken.None);

        BucketStatistics total = (await new IdeaCalibrationRepository(_dataSource).GetAsync(IdeasEngineVersion.Current, 30, null, CalibrationScope.All, CancellationToken.None))!
            .Total!.Outcome;
        (total.Cases, total.Expectancy).ShouldBe((32, 0.5m));
        total.ExpectancyLow!.Value.ShouldBe(-0.3255m, 0.0001m);
        total.ExpectancyHigh!.Value.ShouldBe(1.3255m, 0.0001m);
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

    // The buckets of the model of the cases fired in the last days: a target is +2 R, a stop −1 R.
    private static IEnumerable<IdeaBucket> Model((DateTimeOffset FiredAt, IdeaStatus Status, IdeaSetup Setup)[] cases, int days)
    {
        var key = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        var buckets = new Dictionary<IdeaBucketKey, ClusteredSums>();
        var parent = new ClusteredSums(SixHours);
        var category = new ClusteredSums(SixHours);

        foreach ((DateTimeOffset firedAt, IdeaStatus status, IdeaSetup setup) in cases.Where(item => item.FiredAt >= Now.AddDays(-days)))
        {
            decimal result = status == IdeaStatus.Target ? 2m : -1m;
            IdeaBucketKey bucket = key with { Setup = setup };

            if (!buckets.TryGetValue(bucket, out ClusteredSums? sums))
            {
                sums = new ClusteredSums(SixHours);
                buckets[bucket] = sums;
            }

            sums.Add(firedAt, status, result);
            parent.Add(firedAt, status, result);
            category.Add(firedAt, status, result);
        }

        Dictionary<BucketModel.Parent, ClusteredSums> parents = new() { [BucketModel.Parent.Of(key)] = parent };

        return BucketModel.Build(buckets, parents, BucketModel.Roots(parents, new Dictionary<MarketCategory, ClusteredSums> { [MarketCategory.Linear] = category }))
            // Without fees the expectancy without costs is that of the model.
            .Select(bucket => new IdeaBucket(
                bucket.Key,
                days,
                bucket.Value with { GrossExpectancy = bucket.Value.Expectancy, GrossExpectancyLow = bucket.Value.ExpectancyLow, GrossExpectancyHigh = bucket.Value.ExpectancyHigh }));
    }

    private IdeaStatistics Statistics(ScannerSettings? settings = null)
    {
        return new IdeaStatistics(
            new IdeaSignalRepository(_dataSource),
            new IdeaExitRuleRepository(_dataSource),
            new IdeaBucketRepository(_dataSource),
            new IdeaCalibrationRepository(_dataSource),
            new IdeaEngineVersionRepository(_dataSource),
            new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings() } },
            settings ?? new ScannerSettings(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new FakeTimeProvider(Now));
    }
}