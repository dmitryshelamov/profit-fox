using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     Decision 7: one pass over the signals of a version, by key and firing, gives the sums of the buckets over 90 and 30 days and the outcomes
///     the calibration walks, all by the exit rules in force. Long signals on 15 minute candles entered at 100 with the stop at 98, without fees.
/// </summary>
public class IdeaOutcomesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-06-01T00:00:00Z");
    private static readonly Instrument Wif = new Instrument(MarketCategory.Linear, "WIFUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");
    private static readonly FeeRates NoFees = new FeeRates(0m, 0m);

    // Three keys: «Цель 2R» of WIF with a stop 40 days ago, a target 10 days ago and a repeat while it was open; «Откат» of WIF without an
    // entry and with one that expired at +0.5 R; «Цель 2R» of ETH with a target 100 days ago and a fresh signal.
    [Test]
    public async Task Pass_sums_the_buckets_and_gives_the_outcomes_of_the_counted_signals()
    {
        IdeaOutcomesResult result = await IdeaOutcomes.WalkAsync(Signals(), new IdeaExitRules([]), _ => NoFees, Now, CancellationToken.None);

        var twoR = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        IdeaBucketKey pullback = twoR with { Type = IdeaType.Pullback };
        result.Buckets.ShouldBe(
        [
            new IdeaBucket(twoR, 90, TooLittleData(1, 1, 0, 0.5m)),
            new IdeaBucket(twoR, 30, TooLittleData(1, 0, 0, 2m)),
            new IdeaBucket(pullback, 90, TooLittleData(0, 0, 1, 0.5m) with { Signals = 2 }),
            new IdeaBucket(pullback, 30, TooLittleData(0, 0, 1, 0.5m) with { Signals = 2 })
        ], ignoreOrder: true);
        result.Outcomes.ShouldBe(
        [
            new IdeaOutcome(twoR, Now.AddDays(-40), Now.AddDays(-40).AddMinutes(9), IdeaStatus.Stop, -1m),
            new IdeaOutcome(twoR, Now.AddDays(-10), Now.AddDays(-10).AddMinutes(29), IdeaStatus.Target, 2m),
            new IdeaOutcome(pullback, Now.AddDays(-4), Now.AddDays(-4).AddMinutes(29 + 360), IdeaStatus.Expired, 0.5m),
            new IdeaOutcome(twoR with { Category = MarketCategory.Spot }, Now.AddDays(-100), Now.AddDays(-100).AddMinutes(29), IdeaStatus.Target, 2m)
        ], ignoreOrder: true);
    }

    // The rule in force decides: with a window of one candle the pullback that filled after 30 minutes did not enter, and its bucket has no case.
    [Test]
    public async Task Pass_goes_by_the_exit_rules_in_force()
    {
        var rules = new IdeaExitRules([new IdeaExitRule(IdeaType.Pullback, Interval.FifteenMinutes, 24, 2m, 1, Now)]);

        IdeaOutcomesResult result = await IdeaOutcomes.WalkAsync(Signals(), rules, _ => NoFees, Now, CancellationToken.None);

        result.Buckets.ShouldNotContain(bucket => bucket.Key.Type == IdeaType.Pullback);
    }

    // The fees of the category of each case: a target of 2 R less the taker fee on 100 and on 104, over the risk of 2.
    [Test]
    public async Task Pass_takes_the_fees_of_the_category()
    {
        IdeaOutcomesResult result = await IdeaOutcomes.WalkAsync(
            Signals(),
            new IdeaExitRules([]),
            category => category == MarketCategory.Spot ? new FeeRates(0.1m, 0.1m) : NoFees,
            Now,
            CancellationToken.None);

        result.Outcomes.Single(outcome => outcome.Key.Category == MarketCategory.Spot).Result.ShouldBe(2m - (0.204m / 2m));
    }

    // Decision 4: 30 cases of «Импульс» and 30 of «Пролив» of one parent, a day apart, every third a +2 R target, the rest −1 R stops: the
    // buckets have the numbers of the model over their own sums, their parent's and their category's by the hour of their firing, with their
    // own numbers beside, over 90 days and over the 15 and 20 cases of the last 30; the parent is not a bucket of the result.
    [Test]
    public async Task Pass_gives_the_buckets_the_numbers_of_the_model()
    {
        IdeaSignal[] signals =
        [
            .. Enumerable.Range(0, 30).Select(index => Signal(Wif, IdeaSetup.Impulse, -45 + index, index % 3 == 0 ? Path() with { LadderAfter = Ladder(30) } : Path() with { StopAfter = 10 })),
            .. Enumerable.Range(0, 30).Select(index => Signal(Wif, IdeaSetup.Drop, -40 + index, index % 3 == 1 ? Path() with { LadderAfter = Ladder(30) } : Path() with { StopAfter = 10 }))
        ];

        IdeaOutcomesResult result = await IdeaOutcomes.WalkAsync(signals.ToAsyncEnumerable(), new IdeaExitRules([]), _ => NoFees, Now, CancellationToken.None);

        var impulse = new IdeaBucketKey(IdeaSetup.Impulse, IdeaType.TwoR, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        IdeaBucketKey drop = impulse with { Setup = IdeaSetup.Drop };
        result.Buckets.ShouldBe(
        [
            new IdeaBucket(impulse, 90, Model(signals, 90)[impulse]),
            new IdeaBucket(drop, 90, Model(signals, 90)[drop]),
            new IdeaBucket(impulse, 30, Model(signals, 30)[impulse]),
            new IdeaBucket(drop, 30, Model(signals, 30)[drop])
        ], ignoreOrder: true);
        result.Buckets.Single(bucket => bucket.Key == impulse && bucket.Days == 90).Statistics.OwnProbability.ShouldBe(10m / 30m);
        result.Buckets.Single(bucket => bucket.Key == impulse && bucket.Days == 30).Statistics.FewOwnCases.ShouldBeTrue();
    }

    // The model of the signals fired in the last days, without fees: a target is +2 R, a stop −1 R.
    private static IReadOnlyDictionary<IdeaBucketKey, BucketStatistics> Model(IdeaSignal[] signals, int days)
    {
        var buckets = new Dictionary<IdeaBucketKey, ClusteredSums>();
        var parents = new Dictionary<BucketModel.Parent, ClusteredSums>();
        var categories = new Dictionary<MarketCategory, ClusteredSums>();

        foreach (IdeaSignal signal in signals.Where(signal => signal.Path.FiredAt >= Now.AddDays(-days)))
        {
            bool target = signal.Path.LadderAfter[3] is not null;

            foreach (ClusteredSums sums in new[]
                     {
                         buckets.GetValueOrDefault(signal.Bucket) ?? (buckets[signal.Bucket] = new ClusteredSums()),
                         parents.GetValueOrDefault(BucketModel.Parent.Of(signal.Bucket)) ?? (parents[BucketModel.Parent.Of(signal.Bucket)] = new ClusteredSums()),
                         categories.GetValueOrDefault(signal.Bucket.Category) ?? (categories[signal.Bucket.Category] = new ClusteredSums())
                     })
            {
                sums.Add(signal.Path.FiredAt, target ? IdeaStatus.Target : IdeaStatus.Stop, target ? 2m : -1m);
            }
        }

        return BucketModel.Build(buckets, parents, BucketModel.Roots(parents, categories));
    }

    private static async IAsyncEnumerable<IdeaSignal> Signals()
    {
        IdeaSignal[] signals =
        [
            Signal(Wif, IdeaType.TwoR, -40, Path() with { StopAfter = 10 }),
            Signal(Wif, IdeaType.TwoR, -10, Path() with { LadderAfter = Ladder(30) }),
            Signal(Wif, IdeaType.TwoR, -10, Path() with { FiredAt = Now.AddDays(-10).AddMinutes(10) }),
            Signal(Wif, IdeaType.Pullback, -5, Path() with { EntryAfter = null }),
            Signal(Wif, IdeaType.Pullback, -4, Path() with { EntryAfter = 30, Closes = [.. Enumerable.Repeat(101m, 19)] }),
            Signal(Eth, IdeaType.TwoR, -100, Path() with { LadderAfter = Ladder(30) }),
            Signal(Eth, IdeaType.TwoR, 0, Path() with { FiredAt = Now.AddHours(-1), TrackedUntil = Now.AddMinutes(-1) })
        ];

        foreach (IdeaSignal signal in signals)
        {
            await Task.Yield();

            yield return signal;
        }
    }

    // Entered at the open after the firing; the path known for a week unless the signal sets its own times.
    private static SignalPath Path()
    {
        return SignalPath.Fired(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 100m, 98m, 104m), Interval.FifteenMinutes, Now, 0.01m) with
        {
            EntryAfter = 1,
            Closes = [.. Enumerable.Repeat(100m, 19)]
        };
    }

    private static IdeaSignal Signal(Instrument instrument, IdeaSetup setup, int days, SignalPath path)
    {
        return Signal(instrument, IdeaType.TwoR, days, path) with { Setup = setup };
    }

    private static IdeaSignal Signal(Instrument instrument, IdeaType type, int days, SignalPath path)
    {
        DateTimeOffset fired = path.FiredAt == Now ? Now.AddDays(days) : path.FiredAt;
        DateTimeOffset tracked = path.TrackedUntil == Now ? fired.AddDays(14) : path.TrackedUntil;

        return new IdeaSignal(instrument, IdeaSetup.Impulse, path with { Type = type, FiredAt = fired, TrackedUntil = tracked }, 3m, 2m, IdeasEngineVersion.Current);
    }

    // «Мало данных»: the counts and the own expectancy.
    private static BucketStatistics TooLittleData(int targets, int stops, int expired, decimal expectancy)
    {
        return new BucketStatistics(targets + stops + expired, targets, stops, expired, null, null, null, expectancy, null, null) { OwnExpectancy = expectancy };
    }

    // +2 R first touched at the given minute from the entry.
    private static int?[] Ladder(int twoR)
    {
        var ladder = new int?[10];
        ladder[3] = twoR;

        return ladder;
    }
}