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
    private static readonly SetupThresholds Thresholds = new SetupThresholds(6, 3m, 2m, 1.5m, 3m);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-02-29T23:30:00Z");
    private static readonly DateTimeOffset Five = DateTimeOffset.Parse("2024-03-01T05:00:00Z");

    // Scenario "Импульс на 15 минутах": up 7.05 over 6 candles at ATR 2, 2.17 medians of volume. The last candle alone would be an early
    // impulse (1.65 ATR, 3 medians), which an impulse leaves out.
    [Test]
    public void Impulse_moves_over_the_last_candles_and_leaves_out_the_early_impulse()
    {
        SetupReading reading = Reading("impulse", Five)!;

        reading.Atr.ShouldBe(2m);
        reading.Signals.ShouldBe([new SetupSignal(IdeaSetup.Impulse, 3.525m, 13m / 6m, 99.375m, 107.15m)]);
    }

    [Test]
    public void Early_impulse_moves_over_the_last_candle()
    {
        Reading("early", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.EarlyImpulse, 1.65m, 3m, 99.90m, 103.40m)]);
    }

    [Test]
    public void Drop_is_an_impulse_down()
    {
        Reading("drop", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.Drop, 3.525m, 13m / 6m, 92.85m, 100.625m)]);
    }

    [Test]
    public void Early_drop_is_an_early_impulse_down()
    {
        Reading("early-drop", Five)!.Signals.ShouldBe([new SetupSignal(IdeaSetup.EarlyDrop, 1.65m, 3m, 96.60m, 100.10m)]);
    }

    // ATR / price at 05:00 is 2 / 107.05 = 0.01868…; the window of high volatility is the last candle.
    [TestCase(0.0186, true)]
    [TestCase(0.0187, false)]
    public void High_volatility_is_atr_to_price_above_the_percentile_of_the_pair(decimal percentile, bool holds)
    {
        SetupSignal? signal = Reading("impulse", Five, percentile)!.Signals.SingleOrDefault(signal => signal.Setup == IdeaSetup.HighVolatility);

        (signal is not null).ShouldBe(holds);
        signal?.ShouldBe(new SetupSignal(IdeaSetup.HighVolatility, 1.65m, 3m, 103.65m, 107.15m));
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

    private static SetupReading? Reading(string set, DateTimeOffset at, decimal? percentile = null)
    {
        return Detect(IdeaFixtures.Setup(set), at, percentile);
    }

    // The minutes before the end of the last closed minute, and the candles that closed before the last 15 minutes.
    private static SetupReading? Detect(IReadOnlyList<IdeaCandle> minutes, DateTimeOffset at, decimal? percentile)
    {
        IReadOnlyList<IdeaCandle> closed = IdeaCandles.FromMinutes(Interval.FifteenMinutes, minutes, Start, Interval.FifteenMinutes.CandleOpen(at.AddMinutes(-15)));

        return SetupDetector.Detect(closed, [.. minutes.Where(minute => minute.OpenTime < at)], 15, Thresholds, percentile);
    }
}