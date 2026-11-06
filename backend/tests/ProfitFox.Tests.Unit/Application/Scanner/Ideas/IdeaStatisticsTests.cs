using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Backtests;
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

    [SetUp]
    public void SetUp()
    {
        _signals = Substitute.For<IIdeaSignalRepository>();
        _rules = Substitute.For<IIdeaExitRuleRepository>();
        _buckets = Substitute.For<IIdeaBucketRepository>();
        _calibrations = Substitute.For<IIdeaCalibrationRepository>();
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
                    && buckets.All(bucket => bucket.Statistics == BucketStatisticsCalculator.Compute(1, 0, 0, result, result * result, null))),
                null,
                Arg.Any<CancellationToken>());
            _calibrations.ReplaceAsync(
                2,
                Arg.Is<IReadOnlyCollection<CalibrationReport>>(reports => reports!.Count == 6
                    && reports.All(report => report.ComputedAt == Now)
                    && reports.Single(report => report.Days == 30 && report.Category == MarketCategory.Linear).Unchecked == 1),
                Arg.Any<CancellationToken>());
        });
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
        _rules.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new IdeaExitRule(IdeaType.TwoR, Interval.FifteenMinutes, 1, 2m, null, Now)]);

        await Statistics().RecomputeAsync(IdeaType.TwoR, Interval.FifteenMinutes, CancellationToken.None);

        _signals.Received(1).StreamAsync(2, Now.AddDays(-180), (IdeaType.TwoR, Interval.FifteenMinutes), Arg.Any<CancellationToken>());
        await _buckets.Received(1).ReplaceAsync(
            2,
            Arg.Is<IReadOnlyCollection<IdeaBucket>>(buckets => buckets!.All(bucket => bucket.Statistics.Expired == 1)),
            (IdeaType.TwoR, Interval.FifteenMinutes),
            Arg.Any<CancellationToken>());
        await _calibrations.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default!, default);
    }

    private IdeaStatistics Statistics()
    {
        var backtests = new BacktestSettings { DefaultFees = new Dictionary<string, FeeRateSettings> { ["linear"] = new FeeRateSettings { Limit = 0.02m, Market = 0.055m } } };

        return new IdeaStatistics(_signals, _rules, _buckets, _calibrations, backtests, new IdeasEngineVersion(2), new FakeTimeProvider(Now));
    }

    // «Цель 2R» of 15 minutes entered at 100 with the stop at 98, +2 R at 30 minutes; the close of every point of the grid 100.
    private static IdeaSignal Signal(DateTimeOffset firedAt)
    {
        int?[] ladder = new int?[10];
        ladder[3] = 30;
        SignalPath path = SignalPath.Fired(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Interval.FifteenMinutes, firedAt, 0.01m) with
        {
            EntryAfter = 1,
            LadderAfter = ladder,
            Closes = [.. Enumerable.Repeat(100m, 19)],
            TrackedUntil = firedAt.AddDays(14)
        };

        return new IdeaSignal(Wif, IdeaSetup.Impulse, path, 3m, 2m, 2);
    }
}