using static ProfitFox.Tests.Integration.SetupTapes;

namespace ProfitFox.Tests.Integration.Application.Commands.Strategies;

/// <summary>
///     Change add-setup-strategies, tasks 4.2–4.7: the conditions of the setups as the signals of <c>Fixtures/Setups</c>
///     compute them on a closed candle (spec setup-strategies). The value at the last candle of each history is the one a
///     scenario tells of; a condition that cannot hold for want of a zone or of a quiet candle is missing rather than false,
///     and gives no order either way («Событие сетапа»).
/// </summary>
public sealed class SetupSignalTests
{
    // «Мера движения и объёма», «Движение в ATR»: 3.4 from the open is 1.7 ATR of 2.
    [Test]
    public void A_move_is_measured_in_the_atr_of_the_previous_candle()
    {
        SetupTape tape = AtrOfTwo().Add(100, 103.5m, 99.9m, 103.4m, 3200);

        Assert.That(States("early-impulse", tape.Candles, ("earlyMove", 1.7m))[^1], Is.True);
        Assert.That(States("early-impulse", tape.Candles, ("earlyMove", 1.71m))[^1], Is.False);
    }

    // «Свеча не меряет сама себя»: a candle three times as wide with five times the volume is measured by the ATR and the
    // median before it — 1.7 ATR of 2 and 5 medians of 1000.
    [Test]
    public void A_candle_does_not_measure_itself()
    {
        SetupTape tape = AtrOfTwo().Add(100, 106, 100, 103.4m, 5000);

        Assert.That(States("early-impulse", tape.Candles, ("earlyMove", 1.7m), ("earlyVolume", 5))[^1], Is.True);
        Assert.That(States("early-impulse", tape.Candles, ("earlyMove", 1.7m), ("earlyVolume", 5.01m))[^1], Is.False);
    }

    // «Импульс»: from the open of 100 six candles back to 106.5 is 3.25 ATR, with 2.4 medians of volume.
    [Test]
    public void An_impulse()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1, 1.5m);

        List<bool?> states = States("impulse", tape.Candles);

        Assert.That(tape.Last.Close, Is.EqualTo(106.5m));
        Assert.That(states[^1], Is.True);
        Assert.That(States("impulse", tape.Candles, ("move", 3.25m))[^1], Is.True);
        Assert.That(States("impulse", tape.Candles, ("move", 3.26m))[^1], Is.False);
    }

    // «Движение без объёма».
    [Test]
    public void A_move_without_volume_is_no_impulse()
    {
        SetupTape tape = AtrOfTwo().Moves(1500, 1, 1, 1, 1, 1, 1.5m);

        Assert.That(States("impulse", tape.Candles)[^1], Is.False);
    }

    // «Падение»: from 100 to 93 is 3.5 ATR with 2.2 medians.
    [Test]
    public void A_drop()
    {
        SetupTape tape = AtrOfTwo().Moves(2200, -1, -1, -1, -1, -1, -2);

        Assert.That(tape.Last.Close, Is.EqualTo(93m));
        Assert.That(States("drop", tape.Candles)[^1], Is.True);
        Assert.That(States("impulse", tape.Candles)[^1], Is.False);
    }

    // «Одна сильная свеча».
    [Test]
    public void One_strong_candle_is_an_early_impulse()
    {
        SetupTape tape = AtrOfTwo().Add(100, 103.5m, 99.9m, 103.4m, 3200);

        Assert.That(States("impulse", tape.Candles)[^1], Is.False);
        Assert.That(States("early-impulse", tape.Candles)[^1], Is.True);
    }

    // «Уже импульс»: the same candle after five candles up is an impulse, and the idea is that of impulse.
    [Test]
    public void A_strong_candle_of_an_impulse_is_no_early_impulse()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1);
        tape.Add(105, 108.5m, 104.9m, 108.4m, 3200);

        Assert.That(States("impulse", tape.Candles)[^1], Is.True);
        Assert.That(States("early-impulse", tape.Candles)[^1], Is.False);
    }

    [Test]
    public void An_early_drop_is_the_mirror()
    {
        SetupTape tape = AtrOfTwo().Add(100, 100.1m, 96.5m, 96.6m, 3200);

        Assert.That(States("early-drop", tape.Candles)[^1], Is.True);
        Assert.That(States("early-impulse", tape.Candles)[^1], Is.False);
    }

    // «Закрытие за зоной»: above the zone of 100.0–100.3 by more than 0.1 ATR with 1.8 medians.
    [Test]
    public void A_close_beyond_the_zone_is_a_breakout_up()
    {
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.5m, 1800);

        List<bool?> states = States("breakout-up", tape.Candles);

        Assert.That(states[^1], Is.True);
        Assert.That(states[^2], Is.False, "a candle before is known false: the order comes on the breakout");
    }

    // «Запаса не хватило»: 100.35 is not 0.1 ATR above 100.3.
    [Test]
    public void A_close_short_of_the_margin_is_no_breakout()
    {
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.35m, 1800);

        Assert.That(States("breakout-up", tape.Candles)[^1], Is.False);
    }

    // «Пробой вниз».
    [Test]
    public void A_close_below_the_zone_is_a_breakout_down()
    {
        SetupTape tape = ZoneOf100().Add(100.4m, 100.5m, 99.7m, 99.8m, 1600);

        Assert.That(States("breakout-down", tape.Candles)[^1], Is.True);
        Assert.That(States("breakout-up", tape.Candles)[^1], Is.Null, "no zone is above the open of 100.4: the long side is unknown");
    }

    [Test]
    public void A_breakout_without_volume_is_none()
    {
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.5m, 1400);

        Assert.That(States("breakout-up", tape.Candles)[^1], Is.False);
    }

    // «Снятие стопов под зоной».
    [Test]
    public void A_sweep_under_the_zone_and_a_close_back_is_a_false_breakout_long()
    {
        SetupTape tape = ZoneOf100().Add(100.6m, 100.7m, 99.7m, 100.4m);

        List<bool?> states = States("false-breakout-long", tape.Candles);

        Assert.That(states[^1], Is.True);
        Assert.That(states[^2], Is.False);
    }

    // «Прокол мал».
    [Test]
    public void A_short_sweep_is_no_false_breakout()
    {
        SetupTape tape = ZoneOf100().Add(100.6m, 100.7m, 99.9m, 100.4m);

        Assert.That(States("false-breakout-long", tape.Candles)[^1], Is.False);
    }

    // «Закрытие под зоной»: the zone nearest below 99.9 is the one of 98.6, which the candle did not sweep.
    [Test]
    public void A_close_under_the_zone_is_no_return()
    {
        SetupTape tape = ZoneOf100().Add(100.6m, 100.7m, 99.7m, 99.9m);

        Assert.That(States("false-breakout-long", tape.Candles)[^1], Is.False);
    }

    [Test]
    public void A_sweep_over_the_zone_and_a_close_back_is_a_false_breakout_short()
    {
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.0m);

        Assert.That(States("false-breakout-short", tape.Candles)[^1], Is.True);
    }

    // «Покупают, а цена стоит»: a squeeze, 9000 of rising volume against 5000, the price up 0.3 ATR in 20 candles.
    [Test]
    public void Buying_while_the_price_stands_is_an_accumulation()
    {
        (SetupTape tape, decimal atr) = Accumulation(ups: 8, downs: 5);
        tape.Add(tape.Last.Close, 100.4m, 100, 100 + (0.3m * atr));

        Assert.That(States("squeeze", tape.Candles)[^1], Is.True, "a squeeze on the candle");
        Assert.That(States("accumulation", tape.Candles)[^1], Is.True);
        Assert.That(States("distribution", tape.Candles)[^1], Is.False);
    }

    // «Цена уже ушла».
    [Test]
    public void A_price_that_went_is_no_accumulation()
    {
        (SetupTape tape, decimal atr) = Accumulation(ups: 8, downs: 5);
        tape.Add(tape.Last.Close, 100.6m, 100, 100 + (0.8m * atr));

        Assert.That(States("accumulation", tape.Candles)[^1], Is.False);
    }

    // «Перевеса нет»: 6000 against 5000.
    [Test]
    public void Without_a_bias_there_is_no_accumulation_and_no_distribution()
    {
        (SetupTape tape, decimal atr) = Accumulation(ups: 5, downs: 5);
        tape.Add(tape.Last.Close, 100.4m, 100, 100 + (0.3m * atr));

        Assert.That(States("accumulation", tape.Candles)[^1], Is.False);
        Assert.That(States("distribution", tape.Candles)[^1], Is.False);
    }

    // «Тень после импульса»: a shadow of 1.5 over a body of 1 on 4.5 medians.
    [Test]
    public void A_shadow_after_an_impulse_is_an_exhaustion_top()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1).Add(105, 107.5m, 104.9m, 106, 4500);

        Assert.That(States("impulse", tape.Candles)[^1], Is.True);
        Assert.That(States("exhaustion-top", tape.Candles)[^1], Is.True);
        Assert.That(States("exhaustion-top", tape.Candles)[^2], Is.False);
    }

    // «Объёма мало».
    [Test]
    public void Too_little_volume_is_no_exhaustion()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1).Add(105, 107.5m, 104.9m, 106, 3500);

        Assert.That(States("exhaustion-top", tape.Candles)[^1], Is.False);
    }

    // «Тень короче тела».
    [Test]
    public void A_shadow_shorter_than_the_body_is_no_exhaustion()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1).Add(105, 106.5m, 104.9m, 106, 4500);

        Assert.That(States("exhaustion-top", tape.Candles)[^1], Is.False);
    }

    [Test]
    public void A_shadow_under_a_drop_is_an_exhaustion_bottom()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, -1, -1, -1, -1, -1).Add(95, 95.1m, 92.5m, 94, 4500);

        Assert.That(States("exhaustion-bottom", tape.Candles)[^1], Is.True);
    }

    // «Возврат из перекупленности»: a fall of 7 after forty rises takes the RSI from 100 to 65.
    [Test]
    public void A_return_from_overbought()
    {
        SetupTape tape = Rsi(rise: 1, last: -7);
        List<decimal?> rsi = RsiOf(tape);

        Assert.That((rsi[^2], rsi[^1]), Is.EqualTo(((decimal?)100m, (decimal?)65m)));
        Assert.That(States("rsi-overbought-exit", tape.Candles)[^1], Is.True);
        Assert.That(States("rsi-overbought-exit", tape.Candles)[^2], Is.False);
    }

    // «Возврат из перепроданности».
    [Test]
    public void A_return_from_oversold()
    {
        SetupTape tape = Rsi(rise: -1, last: 7);
        List<decimal?> rsi = RsiOf(tape);

        Assert.That((rsi[^2], rsi[^1]), Is.EqualTo(((decimal?)0m, (decimal?)35m)));
        Assert.That(States("rsi-oversold-exit", tape.Candles)[^1], Is.True);
    }

    // «Ещё в зоне»: a fall of 3 leaves the RSI at 81.25.
    [Test]
    public void Still_overbought_is_no_return()
    {
        SetupTape tape = Rsi(rise: 1, last: -3);

        Assert.That(RsiOf(tape)[^1], Is.EqualTo(81.25m));
        Assert.That(States("rsi-overbought-exit", tape.Candles)[^1], Is.False);
    }

    // «Закрытие выше максимума».
    [Test]
    public void A_close_above_the_highest_high_of_200_candles_is_a_new_high()
    {
        SetupTape tape = Highest().Add(108, 110.3m, 107.9m, 110.2m);

        Assert.That(States("new-high", tape.Candles)[^1], Is.True);
        Assert.That(States("new-high", tape.Candles)[^2], Is.False);
    }

    // «Прокол без закрытия».
    [Test]
    public void A_high_above_without_the_close_is_no_new_high()
    {
        SetupTape tape = Highest().Add(108, 110.5m, 107.9m, 109.9m);

        Assert.That(States("new-high", tape.Candles)[^1], Is.False);
    }

    [Test]
    public void A_close_below_the_lowest_low_is_a_new_low()
    {
        SetupTape tape = Highest().Add(108, 108.1m, 104.5m, 104.8m);

        Assert.That(States("new-low", tape.Candles)[^1], Is.True);
    }

    [Test]
    public void A_quiet_candle_has_volume_and_no_body()
    {
        SetupTape tape = AtrOfTwo().Add(100, 101, 99, 100.1m, 3500);

        Assert.That(States("quiet-candle", tape.Candles)[^1], Is.True);
    }

    // «Большое тело»: 3.5 medians over a body of 0.5 ATR.
    [Test]
    public void A_candle_with_a_large_body_is_not_quiet()
    {
        SetupTape tape = AtrOfTwo().Add(100, 101.2m, 99, 101, 3500);

        Assert.That(States("quiet-candle", tape.Candles)[^1], Is.False);
    }

    // «Волатильность выше обычной»: the ATR of the setup over the close rises above the 90th percentile of the 200 before.
    [Test]
    public void A_wider_atr_than_usual_is_high_volatility()
    {
        SetupTape tape = new SetupTape().Steady(230, 100, 2);
        List<bool?> before = States("high-volatility", tape.Candles);
        tape.Steady(1, 100, 4).Steady(1, 100, 2);

        List<bool?> states = States("high-volatility", tape.Candles);

        Assert.That(before[^1], Is.False, "the share of 0.02 is not above the 90th percentile of the same shares");
        Assert.That(states[^1], Is.True);
    }

    // A squeeze for accumulation: 220 candles at 100 with a range of 2, then 19 calm ones with a range of 0.2 and steps of
    // 0.05 — the first flat, then the rises and the falls asked, the rest flat — with a volume of 1000 each, so the rising
    // candles of the window of 20, the next one included, hold 1000 a candle. The ATR of the setup of the next candle too.
    private static (SetupTape Tape, decimal Atr) Accumulation(int ups, int downs)
    {
        SetupTape tape = new SetupTape().Steady(220, 100, 2);
        decimal[] steps = [0, .. Enumerable.Repeat(0.05m, ups), .. Enumerable.Repeat(-0.05m, downs)];
        steps = [.. steps, .. Enumerable.Repeat(0m, 19 - steps.Length)];
        foreach (decimal step in steps)
        {
            decimal open = tape.Last.Close;
            decimal close = open + step;
            tape.Add(open, Math.Max(open, close) + 0.075m, Math.Min(open, close) - 0.075m, close);
        }

        return (tape, Atr(tape.Candles));
    }

    // Forty candles that rise (or fall) by one, then one that moves by the last step.
    private static SetupTape Rsi(decimal rise, decimal last)
    {
        SetupTape tape = new SetupTape().Steady(1, 100, 0.5m);
        foreach (decimal step in Enumerable.Repeat(rise, 40).Append(last))
        {
            decimal open = tape.Last.Close;
            tape.Add(open, Math.Max(open, open + step) + 0.25m, Math.Min(open, open + step) - 0.25m, open + step);
        }

        return tape;
    }

    private static List<decimal?> RsiOf(SetupTape tape)
    {
        return Numbers("""{ "id": "rsi", "kind": "rsi", "inputs": { "length": 14 } }""", "rsi.result", tape.Candles, """{ "from": "c.close", "to": "rsi.value" }""");
    }

    // 210 candles at 108 with a range of 2 and, among them, one high of 110 and one low of 105: the highest high and the
    // lowest low of the 200 before the next candle.
    private static SetupTape Highest()
    {
        SetupTape tape = new SetupTape().Steady(30, 108, 2);
        tape.Add(108, 110, 107, 108).Steady(100, 108, 2).Add(108, 109, 105, 108);
        return tape.Steady(79, 108, 2);
    }
}
