using System.Text.Json.Nodes;

using MediatR;

using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Queries.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

using static ProfitFox.Tests.Integration.SetupTapes;

namespace ProfitFox.Tests.Integration.Application.Commands.Strategies;

/// <summary>
///     Change add-setup-strategies, tasks 4.2–4.6: the strategies of <c>Fixtures/Setups</c> saved by the commands of the
///     application and run on prepared candles — the event of a setup and the plan of its order (spec setup-strategies
///     «Событие сетапа», «План стратегии сетапа» and the scenarios of the setups that tell of the stop).
/// </summary>
[NonParallelizable]
public sealed class SetupPlanTests
{
    private static ISender Sender => BenchServices.Sender;

    [SetUp]
    public Task Reset()
    {
        return BenchDatabase.Reset();
    }

    // «Условие держится три свечи»: the fifth to the seventh candles up are an impulse; the order is on the fifth.
    [Test]
    public async Task A_condition_held_three_candles_gives_one_order_on_the_first()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m);
        SavedSetup impulse = await Save("impulse");

        List<StrategyStep> orders = impulse.Orders(tape.Candles);

        Assert.That(States("impulse", tape.Candles).TakeLast(4), Is.EqualTo(new bool?[] { false, true, true, true }));
        Assert.That(orders.Select(order => (order.Moment, order.Entry)), Is.EqualTo(new[] { (tape.Candles[^3].CloseTime, (EntrySide?)EntrySide.Long) }));
    }

    // «Истории ещё мало»: the windows fill on a candle where the impulse holds at once, so the previous candle was unknown.
    [Test]
    public async Task No_order_on_the_candle_where_the_windows_fill_with_the_condition_true()
    {
        SetupTape tape = new SetupTape().Steady(15, 100, 2).Moves(2400, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m, 1.3m);
        SavedSetup impulse = await Save("impulse");

        List<bool?> states = States("impulse", tape.Candles);

        Assert.That((states[19], states[20]), Is.EqualTo(((bool?)null, (bool?)true)), "the median of 20 at the previous candle is first known at the 21st");
        Assert.That(impulse.Orders(tape.Candles), Is.Empty);
    }

    // «Стоп лонга»: a close of 100, a low of the window of 98 and an ATR of 1 give a stop of 0.035 and a target of 0.07.
    [Test]
    public async Task The_stop_of_a_long_is_beyond_the_low_of_the_window_by_one_and_a_half_atr()
    {
        SetupTape tape = new SetupTape().Steady(30, 98.2m, 1).Add(98.2m, 100.2m, 98, 100, 3500);
        SavedSetup early = await Save("early-impulse");

        StrategyStep order = early.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Stop.Value, order.Target.Value, order.Size, order.After), Is.EqualTo(((EntrySide?)EntrySide.Long, (decimal?)0.035m, (decimal?)0.07m, (decimal?)100m, (TimeSpan?)TimeSpan.FromHours(24))));
    }

    // «Стоп шорта».
    [Test]
    public async Task The_stop_of_a_short_is_beyond_the_high_of_the_window()
    {
        SetupTape tape = new SetupTape().Steady(30, 101.8m, 1).Add(101.8m, 103, 99.8m, 100, 3500);
        SavedSetup early = await Save("early-drop");

        StrategyStep order = early.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Stop.Value, order.Target.Value), Is.EqualTo(((EntrySide?)EntrySide.Short, (decimal?)0.045m, (decimal?)0.09m)));
    }

    // «Срок на двух таймфреймах»: 24 candles are 24 hours on 1h and 96 on 4h.
    [Test]
    public async Task The_life_in_candles_on_two_timeframes()
    {
        SavedSetup early = await Save("early-impulse");

        TimeSpan? hourly = early.Steps(new SetupTape().Steady(3, 100, 2).Candles)[^1].After;
        TimeSpan? fourHourly = early.Steps(new SetupTape(TimeSpan.FromHours(4)).Steady(3, 100, 2).Candles)[^1].After;

        Assert.That((hourly, fourHourly), Is.EqualTo(((TimeSpan?)TimeSpan.FromHours(24), (TimeSpan?)TimeSpan.FromHours(96))));
    }

    // «Падение»: the stop of drop is beyond the highest high of the six candles.
    [Test]
    public async Task The_stop_of_a_drop_is_beyond_the_high_of_the_six_candles()
    {
        SetupTape tape = AtrOfTwo().Moves(2200, -1, -1, -1, -1, -1, -2);
        SavedSetup drop = await Save("drop");

        StrategyStep order = drop.Orders(tape.Candles).Single();

        decimal highest = tape.Candles.TakeLast(6).Max(candle => candle.High);
        Assert.That(highest, Is.EqualTo(100.5m));
        Assert.That((order.Entry, order.Stop.Value), Is.EqualTo(((EntrySide?)EntrySide.Short, (decimal?)((highest - 93 + (1.5m * 2)) / 93))));
    }

    // «Двусторонняя стратегия» and «Пробой вниз»: the short of breakout takes its stop from the high of the candle.
    [Test]
    public async Task A_short_breakout_takes_its_stop_from_the_high()
    {
        SetupTape tape = ZoneOf100().Add(100.4m, 100.5m, 99.7m, 99.8m, 1600);
        SavedSetup breakout = await Save("breakout");

        StrategyStep order = breakout.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Node), Is.EqualTo(((EntrySide?)EntrySide.Short, "enter-short")));
        Assert.That(order.Stop.Value, Is.EqualTo((100.5m - 99.8m + 1.5m) / 99.8m));
        Assert.That(order.Target.Value, Is.EqualTo((100.5m - 99.8m + 1.5m) / 99.8m * 2));
    }

    [Test]
    public async Task A_long_breakout_takes_its_stop_from_the_low()
    {
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.5m, 1800);
        SavedSetup breakout = await Save("breakout");

        StrategyStep order = breakout.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Stop.Value), Is.EqualTo(((EntrySide?)EntrySide.Long, (decimal?)((100.5m - 99.7m + 1.5m) / 100.5m))));
    }

    // «Снятие стопов под зоной»: the long of false-breakout takes its stop from the low of 99.7.
    [Test]
    public async Task A_false_breakout_long_takes_its_stop_from_the_low_of_the_sweep()
    {
        SetupTape tape = ZoneOf100().Add(100.6m, 100.7m, 99.7m, 100.4m);
        SavedSetup falseBreakout = await Save("false-breakout");

        StrategyStep order = falseBreakout.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Stop.Value), Is.EqualTo(((EntrySide?)EntrySide.Long, (decimal?)((100.4m - 99.7m + 1.5m) / 100.4m))));
    }

    // «Выход вверх»: after a squeeze on the previous candle a close of 104.5 is above the 20 candles between 100 and 104.
    [Test]
    public async Task A_close_above_the_range_after_a_squeeze_enters_long_with_the_stop_from_its_low()
    {
        SetupTape tape = Squeeze(calm: true);
        decimal atr = Atr(tape.Candles);
        tape.Add(102, 104.6m, 101.9m, 104.5m);
        SavedSetup squeeze = await Save("squeeze");

        List<StrategyStep> orders = squeeze.Orders(tape.Candles);

        Assert.That(States("squeeze", tape.Candles)[^2], Is.True, "a squeeze on the previous candle");
        Assert.That(orders.Select(order => (order.Moment, order.Entry)), Is.EqualTo(new[] { (tape.Last.CloseTime, (EntrySide?)EntrySide.Long) }));
        Assert.That(orders[0].Stop.Value, Is.EqualTo((104.5m - 100 + (atr * 1.5m)) / 104.5m));
    }

    // «Внутри диапазона».
    [Test]
    public async Task A_close_inside_the_range_gives_no_order()
    {
        SetupTape tape = Squeeze(calm: true).Add(102, 103.1m, 101.9m, 103);
        SavedSetup squeeze = await Save("squeeze");

        Assert.That(squeeze.Orders(tape.Candles), Is.Empty);
    }

    // «Сжатия не было».
    [Test]
    public async Task Without_a_squeeze_a_close_above_the_range_gives_no_order()
    {
        SetupTape tape = Squeeze(calm: false).Add(102, 104.6m, 101.9m, 104.5m);
        SavedSetup squeeze = await Save("squeeze");

        Assert.That(States("squeeze", tape.Candles)[^2], Is.False);
        Assert.That(squeeze.Orders(tape.Candles), Is.Empty);
    }

    // «Тень после импульса»: the short of exhaustion takes its stop from the highest high of the six candles.
    [Test]
    public async Task An_exhaustion_top_takes_its_stop_from_the_high_of_the_six_candles()
    {
        SetupTape tape = AtrOfTwo().Moves(2400, 1, 1, 1, 1, 1).Add(105, 107.5m, 104.9m, 106, 4500);
        SavedSetup exhaustion = await Save("exhaustion");

        StrategyStep order = exhaustion.Orders(tape.Candles).Single();

        Assert.That((order.Entry, order.Stop.Value), Is.EqualTo(((EntrySide?)EntrySide.Short, (decimal?)((107.5m - 106 + 3) / 106))));
    }

    // «Возврат из перекупленности»: the stop of the short is beyond the larger high of the two candles.
    [Test]
    public async Task A_return_from_overbought_takes_its_stop_from_the_higher_of_two_highs()
    {
        SetupTape tape = new SetupTape().Steady(1, 100, 0.5m).Moves(1000, Enumerable.Repeat(1m, 40).ToArray());
        decimal atr = Atr(tape.Candles);
        decimal open = tape.Last.Close;
        tape.Add(open, open + 0.1m, open - 7.25m, open - 7);
        SavedSetup rsi = await Save("rsi-extreme");

        StrategyStep order = rsi.Orders(tape.Candles).Single();

        decimal higher = Math.Max(tape.Candles[^2].High, tape.Last.High);
        Assert.That((order.Entry, order.Stop.Value), Is.EqualTo(((EntrySide?)EntrySide.Short, (decimal?)((higher - tape.Last.Close + (atr * 1.5m)) / tape.Last.Close))));
    }

    // «Выход вверх на второй свече»: the quiet candle went between 99 and 101, the second one after it closed at 101.4.
    [Test]
    public async Task A_close_above_a_quiet_candle_enters_long_with_the_stop_from_its_low()
    {
        SetupTape tape = AtrOfTwo().Add(100, 101, 99, 100.1m, 3500).Add(100.1m, 101, 99, 100.5m).Add(100.5m, 101.6m, 99.6m, 101.4m);
        SavedSetup quiet = await Save("quiet-volume");

        List<StrategyStep> orders = quiet.Orders(tape.Candles);

        Assert.That(orders.Select(order => (order.Moment, order.Entry)), Is.EqualTo(new[] { (tape.Last.CloseTime, (EntrySide?)EntrySide.Long) }));
        Assert.That(orders[0].Stop.Value, Is.EqualTo((101.4m - 99 + 3) / 101.4m));
    }

    // «Срок ожидания вышел»: the first close above the quiet candle is that of the fifth candle after it.
    [Test]
    public async Task A_close_above_a_quiet_candle_after_its_wait_gives_no_order()
    {
        SetupTape tape = AtrOfTwo().Add(100, 101, 99, 100.1m, 3500);
        for (int candle = 0; candle < 4; candle++)
        {
            tape.Add(tape.Last.Close, 101, 99, 100.5m);
        }

        tape.Add(100.5m, 101.6m, 99.6m, 101.4m);
        SavedSetup quiet = await Save("quiet-volume");

        Assert.That(quiet.Orders(tape.Candles), Is.Empty);
    }

    // «Сигнал отдельно от плана»: a second strategy with another plan on the saved breakout-up leaves breakout as it was.
    [Test]
    public async Task A_second_strategy_on_the_same_signal_leaves_the_first_one_alone()
    {
        SavedSetup breakout = await Save("breakout");
        SavedSetup wide = await SetupFixtures.SaveAsync(Sender, BenchServices.Get<ISignalRepository>(), "breakout", name: "breakout-wide", change: TargetOf(3));
        SetupTape tape = ZoneOf100().Add(99.8m, 100.6m, 99.7m, 100.5m, 1800);

        StrategyInternal first = await Sender.Send(new GetStrategyQuery("breakout"));

        Assert.That(first.Version, Is.EqualTo(1));
        Assert.That(
            wide.Strategy.Signals.Select(use => (use.Name, use.Signal)),
            Is.EqualTo(breakout.Strategy.Signals.Select(use => (use.Name, use.Signal))));
        StrategyStep order = wide.Orders(tape.Candles).Single();
        Assert.That(order.Target.Value, Is.EqualTo(order.Stop.Value * 3));
        Assert.That(breakout.Orders(tape.Candles).Single().Target.Value, Is.EqualTo(order.Stop.Value * 2));
    }

    private static Task<SavedSetup> Save(string strategy)
    {
        return SetupFixtures.SaveAsync(Sender, BenchServices.Get<ISignalRepository>(), strategy);
    }

    private static Action<JsonObject> TargetOf(decimal reach)
    {
        return body => body["graph"]!["parameters"]!.AsArray().Select(item => item!.AsObject()).Single(item => (string?)item["name"] == "targetR")["value"] = JsonValue.Create(reach);
    }

    // 220 candles at 102 with a range of 2, then the 20 candles of the range: nine, a top of 104, nine and a bottom of 100.
    // Calm, the nine have a range of 0.5 and the ATR falls, so the previous candle is a squeeze; otherwise they have a
    // range of 2 and the tops a range of 3, and the ATR grows.
    private static SetupTape Squeeze(bool calm)
    {
        SetupTape tape = new SetupTape().Steady(220, 102, 2);
        decimal range = calm ? 0.5m : 2;
        tape.Steady(9, 102, range).Add(102, 104, calm ? 101.9m : 101, 102).Steady(9, 102, range);
        return tape.Add(102, calm ? 102.1m : 103, 100, 102);
    }
}
