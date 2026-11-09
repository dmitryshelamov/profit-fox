using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>Decision 7: the buckets and the calibration from one pass over the signals of the version, by the exit rules applied.</summary>
public class IdeaStatisticsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");

    private IIdeaSignalRepository _signals = null!;
    private IIdeaExitRuleRepository _rules = null!;
    private IIdeaBucketRepository _buckets = null!;
    private IIdeaCalibrationRepository _calibrations = null!;
    private IIdeaEngineVersionRepository _versions = null!;

    [SetUp]
    public void SetUp()
    {
        _signals = Substitute.For<IIdeaSignalRepository>();
        _rules = Substitute.For<IIdeaExitRuleRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _calibrations = Substitute.For<IIdeaCalibrationRepository>();
        _versions = Substitute.For<IIdeaEngineVersionRepository>();
        _rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _signals.StreamAsync(2, Now.AddDays(-180), Arg.Any<(IdeaType, Interval)?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { Signal(Now.AddDays(-1)) }.ToAsyncEnumerable());
    }

    // A target of 2 R a day ago, less the taker fees on 100 and 104: the buckets of 90 and 30 days, then the calibration at the time of the clock.
    [Test]
    public async Task Whole_recompute_replaces_the_buckets_then_the_calibration()
    {
        await Statistics().RecomputeAsync(CancellationToken.None);

        decimal result = (4m - 0.11220m) / 2m;
        Received.InOrder(() =>
        {
            _buckets.ReplaceAsync(
                2,
                Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.Count == 2
                    && buckets.All(bucket => bucket.Statistics == new BucketStatistics(1, 1, 0, 0, null, null, null, result, null, null) { OwnExpectancy = result })),
                null,
                Arg.Any<CancellationToken>());
            _calibrations.ReplaceAsync(
                2,
                Arg.Is<IReadOnlyCollection<CalibrationReport>>(reports => reports!.Count == 12
                    && reports.All(report => report.ComputedAt == Now)
                    && reports.Single(report => report.Days == 30 && report.Category == MarketCategory.Linear && report.Scope == CalibrationScope.All).Unchecked == 1),
                Arg.Any<CancellationToken>());
        });
    }

    // The calibration walks every case of the pass or none: over more cases than the scanner keeps for it, here none, the buckets are replaced
    // and the calibration stays as it was.
    [Test]
    public async Task Whole_recompute_over_more_cases_than_the_calibration_takes_replaces_the_buckets_only()
    {
        await Statistics(new ScannerSettings { CalibrationCases = 0 }).RecomputeAsync(CancellationToken.None);

        await _buckets.Received(1).ReplaceAsync(2, Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.Count == 2), null, Arg.Any<CancellationToken>());
        await _calibrations.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default!, default);
    }

    // Review of tune-idea-rules: a rule applied while the whole recompute ran is in buckets the pass wrote by the rule before it; the pass names
    // its type × interval to recompute again. One that did not change is not named.
    [Test]
    public async Task Whole_recompute_names_the_rules_applied_while_it_ran()
    {
        var applied = new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 48, 2.5m, 8, Now);
        var kept = new IdeaExitRule(IdeaType.TwoR, Interval.OneHour, 48, 3m, null, Now.AddDays(-1));
        _rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([kept], [kept, applied]);

        IReadOnlyList<(IdeaType Type, Interval Interval)> first = await Statistics().RecomputeAsync(CancellationToken.None);
        IReadOnlyList<(IdeaType Type, Interval Interval)> second = await Statistics().RecomputeAsync(CancellationToken.None);

        first.ShouldBe([(IdeaType.Pullback, Interval.FifteenMinutes)]);
        second.ShouldBeEmpty();
    }

    // Requirement "Пересчёт корзин": after a rule is applied only the buckets of its type × interval, by the rule now applied.
    [Test]
    public async Task Recompute_of_a_type_and_interval_replaces_only_its_buckets_by_the_rule_applied()
    {
        IdeaStatistics statistics = Statistics();
        await statistics.RecomputeAsync(CancellationToken.None);
        _buckets.ClearReceivedCalls();
        _calibrations.ClearReceivedCalls();
        _rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new IdeaExitRule(IdeaType.TwoR, Interval.FifteenMinutes, 1, 2m, null, Now)]);

        await statistics.RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        _signals.Received(1).StreamAsync(2, Now.AddDays(-180), (IdeaType.TwoR, Interval.FifteenMinutes), Arg.Any<CancellationToken>());
        await _buckets.Received(1).ReplaceAsync(
            2,
            Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.All(bucket => bucket.Statistics.Expired == 1)),
            (IdeaType.TwoR, Interval.FifteenMinutes),
            Arg.Any<CancellationToken>());
        await _calibrations.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default!, default);
    }

    // Decision 4: the category is the root of every parent, so the pass of a type × interval takes its sums from the last whole pass. 40 cases
    // of «Цель 2R», every other a stop, next to 40 stops of «За экстремум»: the buckets of «Цель 2R» come out of both passes the same.
    [Test]
    public async Task Recompute_of_a_type_and_interval_shrinks_to_the_categories_of_the_whole_pass()
    {
        IdeaSignal[] twoR = [.. Enumerable.Range(0, 40).Select(index => Signal(Now.AddDays(-50).AddHours(index), stop: index % 2 == 1))];
        IdeaSignal[] beyond = [.. Enumerable.Range(0, 40).Select(index => Signal(Now.AddDays(-40).AddHours(index), stop: true, IdeaType.BeyondExtreme))];
        _signals.StreamAsync(2, Now.AddDays(-180), null, Arg.Any<CancellationToken>()).Returns(_ => twoR.Concat(beyond).ToAsyncEnumerable());
        _signals.StreamAsync(2, Now.AddDays(-180), (IdeaType.TwoR, Interval.FifteenMinutes), Arg.Any<CancellationToken>()).Returns(_ => twoR.ToAsyncEnumerable());
        var written = new List<(IReadOnlyCollection<IdeaBucket> Buckets, (IdeaType, Interval)? Only)>();
        await _buckets.ReplaceAsync(2, Arg.Do<IReadOnlyCollection<IdeaBucket>>(buckets => written.Add((buckets, null))), null, Arg.Any<CancellationToken>());
        await _buckets.ReplaceAsync(
            2,
            Arg.Do<IReadOnlyCollection<IdeaBucket>>(buckets => written.Add((buckets, (IdeaType.TwoR, Interval.FifteenMinutes)))),
            (IdeaType.TwoR, Interval.FifteenMinutes),
            Arg.Any<CancellationToken>());
        IdeaStatistics statistics = Statistics();

        await statistics.RecomputeAsync(CancellationToken.None);
        await statistics.RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        written.Count.ShouldBe(2);
        written[1].Buckets.ShouldBe(written[0].Buckets.Where(bucket => bucket.Key.Type == IdeaType.TwoR), ignoreOrder: true);
        written[1].Buckets.ShouldAllBe(bucket => bucket.Statistics.Probability != null);
    }

    // Decision 4: after a restart the pass of a type × interval waits for the first whole pass, which writes its buckets anyway.
    [Test]
    public async Task Recompute_of_a_type_and_interval_before_the_first_whole_pass_writes_nothing()
    {
        await Statistics().RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        await _buckets.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default!, default, default);
    }

    // Scenario "Правило не применялось" and decision 5: a type × interval whose rule was never applied, or applied before the start of the
    // version, is fresh from that start, one applied after it from the time it was applied; without a start written no case is fresh. Every case here is unchecked, too few before it.
    [TestCase(null, true, 1)]
    [TestCase(3, true, 0)]
    [TestCase(-2, true, 1)]
    [TestCase(null, false, 0)]
    public async Task Calibration_after_the_rules_changed_goes_by_the_rule_and_the_start_of_the_version(int? appliedDays, bool started, int fresh)
    {
        DateTimeOffset start = Now.AddDays(-10);
        _versions.GetStartAsync(2, Arg.Any<CancellationToken>()).Returns(started ? start : null);
        _rules.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(appliedDays is { } days ? [new IdeaExitRule(IdeaType.TwoR, Interval.FifteenMinutes, 24, 2m, null, start.AddDays(days))] : []);
        _signals.StreamAsync(2, Now.AddDays(-180), null, Arg.Any<CancellationToken>())
            .Returns(_ => new[] { Signal(start.AddHours(-1)), Signal(start.AddDays(1)) }.ToAsyncEnumerable());
        IReadOnlyCollection<CalibrationReport> reports = [];
        await _calibrations.ReplaceAsync(2, Arg.Do<IReadOnlyCollection<CalibrationReport>>(written => reports = written), Arg.Any<CancellationToken>());

        await Statistics().RecomputeAsync(CancellationToken.None);

        reports.Single(report => report is { Days: 30, Category: null, Scope: CalibrationScope.All }).Unchecked.ShouldBe(2);
        reports.Single(report => report is { Days: 30, Category: null, Scope: CalibrationScope.Fresh }).Unchecked.ShouldBe(fresh);
    }

    // Decision 2 of add-burst-and-auto-limits: the whole recompute counts the pairs of the minutes of the signals it reads, once, and its cases
    // take the burst of their minute; 40 longs of 15 min an hour apart in minutes of 25 pairs are checked in rows «всплеск · есть».
    [Test]
    public async Task Whole_recompute_gives_the_cases_the_burst_of_their_minute()
    {
        IdeaSignal[] signals = [.. Enumerable.Range(0, 40).Select(hour => Signal(Now.AddDays(-5).AddHours(hour), stop: hour % 3 != 0))];
        _signals.StreamAsync(2, Now.AddDays(-180), null, Arg.Any<CancellationToken>()).Returns(_ => signals.ToAsyncEnumerable());
        _signals.CountPairsByMinuteAsync(2, Now.AddDays(-180), Arg.Any<CancellationToken>())
            .Returns(signals.ToDictionary(signal => (signal.Path.FiredAt, IdeaSide.Long, Interval.FifteenMinutes), _ => 25));
        IReadOnlyCollection<CalibrationReport> reports = [];
        await _calibrations.ReplaceAsync(2, Arg.Do<IReadOnlyCollection<CalibrationReport>>(written => reports = written), Arg.Any<CancellationToken>());

        await Statistics().RecomputeAsync(CancellationToken.None);

        CalibrationReport all = reports.Single(report => report is { Days: 30, Category: null, Scope: CalibrationScope.All });
        all.Checked.ShouldBeGreaterThan(0);
        all.Features.Where(slice => slice.Feature == IdeaFeature.Burst).Select(slice => (slice.Present, slice.Total.Outcome.Cases)).ShouldBe([(true, all.Checked)]);
        await _signals.Received(1).CountPairsByMinuteAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // The recompute of a type × interval writes buckets only, which no feature splits, so it counts no pairs.
    [Test]
    public async Task Recompute_of_a_type_and_interval_counts_no_pairs()
    {
        IdeaStatistics statistics = Statistics();
        await statistics.RecomputeAsync(CancellationToken.None);
        _signals.ClearReceivedCalls();

        await statistics.RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        await _signals.DidNotReceiveWithAnyArgs().CountPairsByMinuteAsync(default, default, default);
    }

    private IdeaStatistics Statistics(ScannerSettings? settings = null)
    {
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };

        return new IdeaStatistics(_signals, _rules, _buckets, _calibrations, _versions, backtests, settings ?? new ScannerSettings(), new IdeasEngineVersion(2), new FakeTimeProvider(Now));
    }

    // «Цель 2R» of 15 minutes entered at 100 with the stop at 98, +2 R at 30 minutes or the stop at 10; the close of every point of the grid 100.
    private static IdeaSignal Signal(DateTimeOffset firedAt, bool stop = false, IdeaType type = IdeaType.TwoR)
    {
        int?[] ladder = new int?[10];
        ladder[3] = stop ? null : 30;
        SignalPath path = SignalPath.Fired(new IdeaPlan(type, IdeaSide.Long, 100m, 98m, 104m), Interval.FifteenMinutes, firedAt, 0.01m) with
        {
            EntryAfter = 1,
            StopAfter = stop ? 10 : null,
            LadderAfter = ladder,
            Closes = [.. Enumerable.Repeat(100m, 19)],
            TrackedUntil = firedAt.AddDays(14)
        };

        return new IdeaSignal(Wif, IdeaSetup.Impulse, path, 3m, 2m, 2);
    }
}