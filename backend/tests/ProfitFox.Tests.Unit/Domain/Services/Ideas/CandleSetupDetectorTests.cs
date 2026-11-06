using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Requirements of the candle setups on the scenarios of the spec, with the thresholds d = 0.1, Vп = 1.5, e = 0.2, Nс = 20, Xк = 4, w = 1,
///     Xт = 3, Bт = 0.3 and RSI 70 / 30. The median volume of a candle is 1000; the last candle is the window of the last minutes of the
///     interval's length.
/// </summary>
public class CandleSetupDetectorTests
{
    private const decimal Median = 1000m;

    private static readonly SetupThresholds Thresholds = new SetupThresholds(6, 3m, 2m, 1.5m, 3m, 0.1m, 1.5m, 0.2m, 20, 4m, 1m, 3m, 0.3m, 70m, 30m);
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2024-03-01T10:00:00Z");
    private static readonly Zone Resistance = new Zone(100.0m, 100.6m, 2);

    // Scenario "Пробой сопротивления": ATR 2, open 99.8 at the zone, the close 101.0 is 0.4 above its high, 2 medians of volume.
    [Test]
    public void Breakout_closes_beyond_the_zone_with_a_margin_and_volume()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.1m, 99.7m, 101.0m, 2), 2m, [Resistance]))
            .ShouldBe(new SetupSignal(IdeaSetup.Breakout, IdeaSide.Long, 0.6m, 2m, 99.7m, 101.1m, Resistance));
    }

    // Scenario "Пробой без запаса": 100.7 is only 0.1 above the zone, d × ATR is 0.2.
    [Test]
    public void Breakout_without_the_margin_is_none()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.1m, 99.7m, 100.7m, 2), 2m, [Resistance])).ShouldBeNull();
    }

    [Test]
    public void Breakout_without_the_volume_is_none()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.1m, 99.7m, 101.0m, 1.4m), 2m, [Resistance])).ShouldBeNull();
    }

    // A candle that opened above the zone did not break it: it was beyond already.
    [Test]
    public void Breakout_of_a_candle_that_opened_beyond_the_zone_is_none()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(100.7m, 101.1m, 100.65m, 101.0m, 2), 2m, [Resistance])).ShouldBeNull();
    }

    [Test]
    public void Breakout_down_closes_below_a_support()
    {
        var support = new Zone(99.4m, 100.0m, 2);

        Signal(IdeaSetup.Breakout, Input(Candle(100.2m, 100.3m, 98.9m, 99.0m, 2), 2m, [support]))
            .ShouldBe(new SetupSignal(IdeaSetup.Breakout, IdeaSide.Short, 0.6m, 2m, 98.9m, 100.3m, support));
    }

    // Through two zones the zone of the setup is the one nearest to the close.
    [Test]
    public void Breakout_through_two_zones_takes_the_one_nearest_to_the_close()
    {
        var upper = new Zone(101.0m, 101.2m, 3);

        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.7m, 99.7m, 101.6m, 2), 2m, [Resistance, upper]))!.Zone.ShouldBe(upper);
    }

    // Scenario "Вынос поддержки с возвратом в зону": ATR 1, open 50.6, the low 49.7 is 0.3 below the zone, the close 50.2 back in it.
    [Test]
    public void False_breakout_of_a_support_sweeps_below_it_and_closes_back()
    {
        var support = new Zone(50.0m, 50.4m, 2);

        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.7m, 50.2m, 1), 1m, [support]))
            .ShouldBe(new SetupSignal(IdeaSetup.FalseBreakout, IdeaSide.Long, 0.4m, 1m, 49.7m, 50.7m, support));
    }

    // Scenario "Вынос без возврата".
    [Test]
    public void False_breakout_without_the_close_back_is_none()
    {
        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.7m, 49.9m, 1), 1m, [new Zone(50.0m, 50.4m, 2)])).ShouldBeNull();
    }

    // The low 49.9 is only 0.1 below the zone, e × ATR is 0.2.
    [Test]
    public void False_breakout_without_a_deep_enough_sweep_is_none()
    {
        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.9m, 50.2m, 1), 1m, [new Zone(50.0m, 50.4m, 2)])).ShouldBeNull();
    }

    // A candle that opened below the support came from beyond it: no sweep.
    [Test]
    public void False_breakout_of_a_candle_that_opened_beyond_the_zone_is_none()
    {
        Signal(IdeaSetup.FalseBreakout, Input(Candle(49.9m, 50.5m, 49.7m, 50.2m, 1), 1m, [new Zone(50.0m, 50.4m, 2)])).ShouldBeNull();
    }

    [Test]
    public void False_breakout_of_a_resistance_sweeps_above_it_and_gives_short()
    {
        var resistance = new Zone(50.0m, 50.4m, 2);

        Signal(IdeaSetup.FalseBreakout, Input(Candle(49.8m, 50.7m, 49.7m, 50.2m, 1), 1m, [resistance]))
            .ShouldBe(new SetupSignal(IdeaSetup.FalseBreakout, IdeaSide.Short, 0.4m, 1m, 49.7m, 50.7m, resistance));
    }

    // Both supports swept and closed back above: the zone of the setup is the one nearest to the close.
    [Test]
    public void False_breakout_of_two_zones_takes_the_one_nearest_to_the_close()
    {
        var near = new Zone(50.0m, 50.4m, 2);

        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.1m, 50.2m, 1), 1m, [new Zone(49.4m, 49.5m, 2), near]))!.Zone.ShouldBe(near);
    }

    // Scenario "Тихий рынок": ATR / price 0.4 / 100 = 0.4 % below the 10th percentile 0.5 %. The range is the last 20 closed candles, 99.2 …
    // 100.8, and the last candle up to 101.2; the 5 wide candles before them are left out.
    [Test]
    public void Squeeze_ranges_over_the_last_closed_candles_and_the_last_one()
    {
        CandleSetupInput input = Quiet(0.005m);

        Signal(IdeaSetup.Squeeze, input).ShouldBe(new SetupSignal(IdeaSetup.Squeeze, null, 0.5m, 1m, 99.2m, 101.2m));
    }

    [TestCase(0.004)]
    [TestCase(null)]
    public void Squeeze_needs_atr_to_price_below_the_tenth_percentile(decimal? percentile)
    {
        Signal(IdeaSetup.Squeeze, Quiet(percentile)).ShouldBeNull();
    }

    // Scenario "Климакс с тенью": an impulse, 5 medians of volume, a body of 0.8 and an upper wick of 1.1. The window is the impulse's.
    [Test]
    public void Exhaustion_of_a_rise_is_an_impulse_with_a_climax_and_a_wick_against_it()
    {
        var impulse = new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.5m, 2.2m, 96m, 101.9m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.9m, 99.9m, 100.8m, 5), 2m, []) with { Moves = [impulse] })
            .ShouldBe(impulse with { Setup = IdeaSetup.Exhaustion, Side = IdeaSide.Short });
    }

    // Scenario "Импульс без тени".
    [Test]
    public void Exhaustion_without_the_wick_is_none()
    {
        var impulse = new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.5m, 2.2m, 96m, 101.1m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.1m, 99.9m, 100.8m, 5), 2m, []) with { Moves = [impulse] }).ShouldBeNull();
    }

    [Test]
    public void Exhaustion_without_the_climax_is_none()
    {
        var impulse = new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.5m, 2.2m, 96m, 101.9m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.9m, 99.9m, 100.8m, 3.9m), 2m, []) with { Moves = [impulse] }).ShouldBeNull();
    }

    // A candle without a body or a wick has no wick of w bodies against the move.
    [Test]
    public void Exhaustion_needs_a_wick()
    {
        var impulse = new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.5m, 2.2m, 96m, 100m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 100m, 99.9m, 100m, 5), 2m, []) with { Moves = [impulse] }).ShouldBeNull();
    }

    [Test]
    public void Without_an_impulse_there_is_no_exhaustion()
    {
        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.9m, 99.9m, 100.8m, 5), 2m, [])).ShouldBeNull();
    }

    // «Истощение» stands on «Импульс» and «Падение» only.
    [Test]
    public void Early_impulse_and_high_volatility_give_no_exhaustion()
    {
        var early = new SetupSignal(IdeaSetup.EarlyImpulse, IdeaSide.Long, 1.6m, 5m, 99.9m, 101.9m);
        var high = new SetupSignal(IdeaSetup.HighVolatility, null, 1.6m, 5m, 99.9m, 101.9m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.9m, 98.1m, 100.8m, 5), 2m, []) with { Moves = [early, high] }).ShouldBeNull();
    }

    [Test]
    public void Exhaustion_of_a_drop_has_the_lower_wick_and_gives_long()
    {
        var drop = new SetupSignal(IdeaSetup.Drop, IdeaSide.Short, 3.5m, 2.2m, 98.1m, 104m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 100.1m, 98.1m, 99.2m, 5), 2m, []) with { Moves = [drop] })
            .ShouldBe(drop with { Setup = IdeaSetup.Exhaustion, Side = IdeaSide.Long });
    }

    // Scenario "Выход из перекупленности": RSI of the closed candles 74 (an average gain of 0.74 against a loss of 0.26), with the last candle
    // down 1.2 it is 67.7. The window is the last closed candle and the last one.
    [Test]
    public void Overbought_is_rsi_of_the_closed_candles_at_seventy_and_with_the_last_one_below()
    {
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.74m, 0.26m, 100m), 98.8m))
            .ShouldBe(new SetupSignal(IdeaSetup.RsiExtreme, IdeaSide.Short, 0.6m, 1m, 98.6m, 100.5m));
    }

    // Scenario "Ещё в зоне": with the last candle down 0.5 RSI is 71.3.
    [Test]
    public void Rsi_still_in_the_zone_is_none()
    {
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.74m, 0.26m, 100m), 99.5m)).ShouldBeNull();
    }

    // RSI 26, with the last candle up 1.2 it is 32.3.
    [Test]
    public void Oversold_left_gives_long()
    {
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.26m, 0.74m, 100m), 101.2m))!.Side.ShouldBe(IdeaSide.Long);
    }

    [Test]
    public void Bounds_of_rsi_come_from_the_thresholds()
    {
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.74m, 0.26m, 100m), 98.8m), Thresholds with { Overbought = 75m }).ShouldBeNull();
    }

    // Scenario "Максимум за 200 свечей": the high of the last 200 closed candles is 120, one candle before them reached 130.
    [Test]
    public void New_high_closes_above_the_high_of_the_last_two_hundred_closed_candles()
    {
        Signal(IdeaSetup.NewExtreme, Extremes(201, 120.5m)).ShouldBe(new SetupSignal(IdeaSetup.NewExtreme, IdeaSide.Long, 0.25m, 1m, 119.9m, 120.6m));
    }

    [Test]
    public void New_extreme_needs_two_hundred_closed_candles()
    {
        Signal(IdeaSetup.NewExtreme, Extremes(199, 120.5m)).ShouldBeNull();
    }

    [Test]
    public void New_low_closes_below_the_low_of_the_last_two_hundred_closed_candles_and_gives_short()
    {
        Signal(IdeaSetup.NewExtreme, Extremes(201, 99.5m))!.Side.ShouldBe(IdeaSide.Short);
    }

    // Scenario "Набор позиции": ATR 2, 4 medians of volume, a body of 0.4 under 0.3 × 2; the range is the last candle.
    [Test]
    public void Quiet_volume_is_a_lot_of_volume_with_a_small_body()
    {
        Signal(IdeaSetup.QuietVolume, Input(Candle(100m, 100.9m, 99.3m, 100.4m, 4), 2m, []))
            .ShouldBe(new SetupSignal(IdeaSetup.QuietVolume, null, 0.2m, 4m, 99.3m, 100.9m));
    }

    [Test]
    public void Quiet_volume_with_a_body_of_0_6_at_atr_2_is_none()
    {
        Signal(IdeaSetup.QuietVolume, Input(Candle(100m, 100.9m, 99.3m, 100.6m, 4), 2m, [])).ShouldBeNull();
    }

    [Test]
    public void Quiet_volume_without_the_volume_is_none()
    {
        Signal(IdeaSetup.QuietVolume, Input(Candle(100m, 100.9m, 99.3m, 100.4m, 2.9m), 2m, [])).ShouldBeNull();
    }

    // 200 or 199 candles from 100 to 110, the 150th from the end up to 120 and down to 100; one more before them from 90 to 130.
    private static CandleSetupInput Extremes(int count, decimal close)
    {
        IdeaCandle[] closed =
        [
            .. Enumerable.Range(0, count).Select(index => index == 0 && count > 200 ? Candle(110m, 130m, 90m, 110m, 1)
                : index == count - 150 ? Candle(105m, 120m, 100m, 105m, 1)
                : Candle(105m, 110m, 101m, 105m, 1))
        ];
        decimal open = close > 110m ? 120m : 100m;

        return Input(Candle(open, Math.Max(open, close) + 0.1m, Math.Min(open, close) - 0.1m, close, 1), 2m, []) with { Closed = closed };
    }

    private static CandleSetupInput Rsi(RsiState rsi, decimal close)
    {
        IdeaCandle closed = Candle(99.5m, 100.5m, 99.4m, 100m, 1);

        return Input(Candle(100m, Math.Max(close, 100m) + 0.2m, Math.Min(close, 100m) - 0.2m, close, 1), 2m, []) with { Closed = [closed], Rsi = rsi };
    }

    private static CandleSetupInput Quiet(decimal? percentile)
    {
        IdeaCandle[] wide = [.. Enumerable.Range(0, 5).Select(_ => Candle(100m, 110m, 90m, 100m, 1))];
        IdeaCandle[] narrow = [.. Enumerable.Range(0, 20).Select(index => Candle(100m, index == 3 ? 100.8m : 100.5m, index == 7 ? 99.2m : 99.5m, 100m, 1))];

        return Input(Candle(99.8m, 101.2m, 99.6m, 100m, 1), 0.4m, []) with { Closed = [.. wide, .. narrow], LowPercentile = percentile };
    }

    // The closed candles had no trades: no median to measure the volume by, so no setup needs volume, as with the setups of a move.
    [Test]
    public void Without_a_median_the_volume_is_none()
    {
        CandleSetupInput input = Input(Candle(100m, 100.9m, 99.3m, 100.4m, 4), 2m, []) with { Median = 0m };

        CandleSetupDetector.Detect(input, Thresholds).ShouldNotContain(signal => signal.Setup == IdeaSetup.QuietVolume);
    }

    // The bounds of the conditions hold at equality: «не меньше», «не выше», «не ниже».
    [Test]
    public void Breakout_holds_at_exactly_its_volume()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.1m, 99.7m, 101.0m, 1.5m), 2m, [Resistance])).ShouldNotBeNull();
    }

    // The close 100.8 is exactly d × ATR = 0.2 above the zone; the open 100.6 is exactly at its high.
    [Test]
    public void Breakout_holds_at_exactly_its_margin_and_with_the_open_at_the_edge()
    {
        Signal(IdeaSetup.Breakout, Input(Candle(99.8m, 101.1m, 99.7m, 100.8m, 2), 2m, [Resistance])).ShouldNotBeNull();
        Signal(IdeaSetup.Breakout, Input(Candle(100.6m, 101.1m, 100.5m, 101.0m, 2), 2m, [Resistance])).ShouldNotBeNull();
    }

    // Down through 99.4–100.0: the close 99.2 is exactly 0.2 below the zone; the open 99.4 is exactly at its low.
    [Test]
    public void Breakout_down_holds_at_exactly_its_margin_and_with_the_open_at_the_edge()
    {
        var support = new Zone(99.4m, 100.0m, 2);

        Signal(IdeaSetup.Breakout, Input(Candle(100.2m, 100.3m, 99.1m, 99.2m, 2), 2m, [support]))!.Side.ShouldBe(IdeaSide.Short);
        Signal(IdeaSetup.Breakout, Input(Candle(99.4m, 99.5m, 98.9m, 99.0m, 2), 2m, [support]))!.Side.ShouldBe(IdeaSide.Short);
    }

    [Test]
    public void Breakout_down_through_two_zones_takes_the_one_nearest_to_the_close()
    {
        var lower = new Zone(98.0m, 98.3m, 3);

        Signal(IdeaSetup.Breakout, Input(Candle(100.2m, 100.3m, 97.4m, 97.5m, 2), 2m, [new Zone(99.4m, 100.0m, 2), lower]))!.Zone.ShouldBe(lower);
    }

    // ATR 1, e = 0.2: the low 49.8 is exactly 0.2 below the support 50.0–50.4, the high 50.6 exactly 0.2 above the resistance.
    [Test]
    public void False_breakout_holds_at_exactly_its_sweep()
    {
        var zone = new Zone(50.0m, 50.4m, 2);

        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.8m, 50.2m, 1), 1m, [zone]))!.Side.ShouldBe(IdeaSide.Long);
        Signal(IdeaSetup.FalseBreakout, Input(Candle(49.8m, 50.6m, 49.7m, 50.2m, 1), 1m, [zone]))!.Side.ShouldBe(IdeaSide.Short);
    }

    // A close at the very edge it swept is not back beyond it.
    [Test]
    public void False_breakout_that_closes_at_the_edge_is_none()
    {
        var zone = new Zone(50.0m, 50.4m, 2);

        Signal(IdeaSetup.FalseBreakout, Input(Candle(50.6m, 50.7m, 49.7m, 50.0m, 1), 1m, [zone])).ShouldBeNull();
        Signal(IdeaSetup.FalseBreakout, Input(Candle(49.8m, 50.7m, 49.7m, 50.4m, 1), 1m, [zone])).ShouldBeNull();
    }

    // Exactly 4 medians of volume, and an upper wick of exactly one body, 0.8.
    [Test]
    public void Exhaustion_holds_at_exactly_its_climax_and_its_wick()
    {
        var impulse = new SetupSignal(IdeaSetup.Impulse, IdeaSide.Long, 3.5m, 2.2m, 96m, 101.9m);

        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.9m, 99.9m, 100.8m, 4), 2m, []) with { Moves = [impulse] }).ShouldNotBeNull();
        Signal(IdeaSetup.Exhaustion, Input(Candle(100m, 101.6m, 99.9m, 100.8m, 5), 2m, []) with { Moves = [impulse] }).ShouldNotBeNull();
    }

    // RSI 80 of the closed candles (a gain of 0.8 against a loss of 0.2) and 50 with the last candle down 7.8; mirrored, 20 and 50 up 7.8.
    // The bounds are set at these values, so each holds at equality.
    [Test]
    public void Rsi_bounds_hold_at_equality_for_the_closed_candles_and_not_for_the_last_one()
    {
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.8m, 0.2m, 100m), 92.2m), Thresholds with { Overbought = 80m })!.Side.ShouldBe(IdeaSide.Short);
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.8m, 0.2m, 100m), 92.2m), Thresholds with { Overbought = 50m }).ShouldBeNull();
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.2m, 0.8m, 100m), 107.8m), Thresholds with { Oversold = 20m })!.Side.ShouldBe(IdeaSide.Long);
        Signal(IdeaSetup.RsiExtreme, Rsi(new RsiState(0.2m, 0.8m, 100m), 107.8m), Thresholds with { Oversold = 50m }).ShouldBeNull();
    }

    [Test]
    public void New_extreme_holds_with_exactly_two_hundred_closed_candles()
    {
        Signal(IdeaSetup.NewExtreme, Extremes(200, 120.5m))!.Side.ShouldBe(IdeaSide.Long);
    }

    [Test]
    public void Quiet_volume_holds_at_exactly_its_volume()
    {
        Signal(IdeaSetup.QuietVolume, Input(Candle(100m, 100.9m, 99.3m, 100.4m, 3), 2m, [])).ShouldNotBeNull();
    }

    private static SetupSignal? Signal(IdeaSetup setup, CandleSetupInput input, SetupThresholds? thresholds = null)
    {
        return CandleSetupDetector.Detect(input, thresholds ?? Thresholds).SingleOrDefault(signal => signal.Setup == setup);
    }

    private static CandleSetupInput Input(IdeaCandle last, decimal atr, IReadOnlyList<Zone> zones)
    {
        return new CandleSetupInput([], last, atr, Median, zones, null, null, []);
    }

    private static IdeaCandle Candle(decimal open, decimal high, decimal low, decimal close, decimal medians)
    {
        return new IdeaCandle(At, open, high, low, close, medians * Median);
    }
}