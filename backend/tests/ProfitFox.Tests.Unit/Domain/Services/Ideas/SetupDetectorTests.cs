using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Fixtures.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements "Сетапы", "Условия сетапов" and "Срабатывание сетапа" on the 15 minute sets of <see cref="IdeaFixtures.Setup" />, with the
///     thresholds N = 6, A = 3, V = 2, a = 1.5, v = 3. ATR and the median come from the closed candles before the last one, which is the window
///     of the last 15 minutes.
/// </summary>
public class SetupDetectorTests
{
    private static readonly SetupThresholds Thresholds = new SetupThresholds(6, 3m, 2m, 1.5m, 3m, 0.1m, 1.5m, 0.2m, 20, 4m, 1m, 3m, 0.3m, 1.5m, 0.5m, 70m, 30m);
    private static readonly DateTimeOffset Five = DateTimeOffset.Parse("2024-03-01T05:00:00Z");
    private static readonly DateTimeOffset Breakout = DateTimeOffset.Parse("2024-03-01T07:30:00Z");

    // Scenario "Импульс на 15 минутах": up 7.05 over 6 candles at ATR 2, 2.17 medians of volume. The last candle alone would be an early
    // impulse (1.65 ATR, 3 medians), which an impulse leaves out.
    [Test]
    public void Impulse_moves_over_the_last_candles_and_leaves_out_the_early_impulse()
    {
        SetupReading reading = Reading("impulse", Five)!;

        reading.Atr.ShouldBe(2m);
        reading.Signals.ShouldBe([new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.525m, 13m / 6m, 99.375m, 107.15m)]);
    }

    [Test]
    public void Early_impulse_moves_over_the_last_candle()
    {
        Reading("early", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.EarlyImpulse, IdeaSide.Long, 1.65m, 3m, 99.90m, 103.40m)]);
    }

    [Test]
    public void Drop_is_an_impulse_down()
    {
        Reading("drop", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.Drop, IdeaSide.Short, 3.525m, 13m / 6m, 92.85m, 100.625m)]);
    }

    [Test]
    public void Early_drop_is_an_early_impulse_down()
    {
        Reading("early-drop", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.EarlyDrop, IdeaSide.Short, 1.65m, 3m, 96.60m, 100.10m)]);
    }

    // ATR / price at 05:00 is 2 / 107.05 = 0.01868…; the window of high volatility is the last candle.
    [TestCase(0.0186, true)]
    [TestCase(0.0187, false)]
    public void High_volatility_is_atr_to_price_above_the_percentile_of_the_pair(decimal percentile, bool holds)
    {
        SetupSignal? signal = Reading("impulse", Five, percentile)!.Signals.SingleOrDefault(signal => signal.Setup == IdeaSetup.HighVolatility);

        (signal is not null).ShouldBe(holds);
        signal?.ShouldBe(new SetupSignal(IdeaSetup.HighVolatility, null, 1.65m, 3m, 103.65m, 107.15m));
    }

    // Requirement "Идеи сетапа": the signal knows its sides; high volatility gives both.
    [Test]
    public void High_volatility_gives_ideas_of_both_sides()
    {
        SetupSignal signal = Reading("impulse", Five, 0.0186m)!.Signals.Single(signal => signal.Setup == IdeaSetup.HighVolatility);

        signal.Sides.ShouldBe([IdeaSide.Long, IdeaSide.Short]);
    }

    [TestCase("impulse", IdeaSide.Long)]
    [TestCase("drop", IdeaSide.Short)]
    public void Impulse_gives_long_and_drop_short(string set, IdeaSide side)
    {
        Reading(set, Five)!.Signals.Single().Sides.ShouldBe([side]);
    }

    // Scenario "Мало истории": at 04:30 only 19 candles closed before the last one.
    [Test]
    public void Fewer_than_twenty_closed_candles_give_no_setups()
    {
        Reading("impulse", Five.AddMinutes(-30)).ShouldBeNull();
    }

    [Test]
    public void Without_volume_only_high_volatility_can_hold()
    {
        IdeaCandle[] minutes = [.. IdeaFixtures.Setup("impulse").Select(minute => minute with { Volume = 0m })];

        Detect(minutes, Five, 0.0001m)!.Signals.Select(signal => (signal.Setup, signal.VolumeRatio)).ShouldBe([(IdeaSetup.HighVolatility, 0m)]);
    }

    [Test]
    public void A_pair_whose_price_stands_still_has_no_setups()
    {
        IdeaCandle[] minutes = [.. IdeaFixtures.Setup("impulse").Select(minute => minute with { Open = 100m, High = 100m, Low = 100m, Close = 100m })];

        Detect(minutes, Five, 0.0001m).ShouldBeNull();
    }

    // Scenario "Условие держится": the impulse holds from 04:56 to the end of the set, and fires once.
    [Test]
    public void Setup_fires_once_on_the_first_minute_its_condition_holds()
    {
        var fired = new List<(DateTimeOffset, IdeaSetup)>();
        IdeaSetup[] before = [];

        for (DateTimeOffset at = Five.AddMinutes(-90); at <= Five.AddMinutes(10); at = at.AddMinutes(1))
        {
            SetupReading? reading = Reading("impulse", at);
            fired.AddRange(SetupDetector.Fired(before, reading).Select(signal => (at, signal.Setup)));
            before = [.. reading?.Signals.Select(signal => signal.Setup) ?? []];
        }

        fired.ShouldBe([(DateTimeOffset.Parse("2024-03-01T04:56:00Z"), IdeaSetup.Impulse)]);
    }

    // Requirement "Срабатывание сетапа" for a candle setup: the breakout of 101.6–101.8 holds from 07:33 to 07:42 and fires once.
    [Test]
    public void Candle_setup_that_holds_fires_once()
    {
        var fired = new List<(DateTimeOffset, IdeaSetup)>();
        var held = new List<DateTimeOffset>();
        IdeaSetup[] before = [];

        for (DateTimeOffset at = Breakout.AddMinutes(-10); at <= Breakout.AddMinutes(30); at = at.AddMinutes(1))
        {
            SetupReading? reading = Detect(IdeaFixtures.Breakout(), at, null);
            fired.AddRange(SetupDetector.Fired(before, reading).Where(signal => signal.Setup == IdeaSetup.Breakout).Select(signal => (at, signal.Setup)));
            held.AddRange(reading!.Signals.Where(signal => signal.Setup == IdeaSetup.Breakout).Select(_ => at));
            before = [.. reading.Signals.Select(signal => signal.Setup)];
        }

        fired.ShouldBe([(Breakout.AddMinutes(4), IdeaSetup.Breakout)]);
        held.Count.ShouldBe(10);
    }

    // The base carries the high and the low of the last 200 closed candles, the first of their value as Max and Min take them, none with fewer.
    [Test]
    public void Base_carries_the_extremes_of_the_last_two_hundred_closed_candles()
    {
        IdeaCandle[] closed =
        [
            .. Enumerable.Range(0, 230).Select(index => new IdeaCandle(Five.AddMinutes(15 * index), 100m, index == 10 ? 150m : index == 40 ? 120.0m : index == 60 ? 120.00m : 110m, index == 50 ? 90.0m : index == 70 ? 90.00m : 95m, 100m, 1m))
        ];

        CandleExtremes extremes = SetupDetector.Base(closed, ZoneRules.Default)!.Extremes!;

        (extremes.High.ToString(), extremes.Low.ToString()).ShouldBe(("120.0", "90.0"));
        SetupDetector.Base(closed[..199], ZoneRules.Default)!.Extremes.ShouldBeNull();
    }

    private static SetupReading? Reading(string set, DateTimeOffset at, decimal? percentile = null)
    {
        return Detect(IdeaFixtures.Setup(set), at, percentile);
    }

    // The minutes before the end of the last closed minute, and the candles that closed before the last 15 minutes.
    private static SetupReading? Detect(IReadOnlyList<IdeaCandle> minutes, DateTimeOffset at, decimal? percentile)
    {
        IReadOnlyList<IdeaCandle> closed = IdeaCandles.FromMinutes(Interval.FifteenMinutes, minutes, minutes[0].OpenTime, Interval.FifteenMinutes.CandleOpen(at.AddMinutes(-15)));

        return SetupDetector.Base(closed, ZoneRules.Default) is { } basis
            ? SetupDetector.Detect(basis, [.. minutes.Where(minute => minute.OpenTime < at)], 15, Thresholds, new VolatilityPercentiles(null, percentile))
            : null;
    }
}