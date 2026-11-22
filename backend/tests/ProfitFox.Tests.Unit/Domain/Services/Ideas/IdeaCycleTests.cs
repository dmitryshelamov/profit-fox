using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, tasks 7.2 and 7.4: spec strategy-ideas «Цена входа идеи», «Итог идеи», «Результат
///     сейчас», «Исключённая идея» and «Перезапуск и простой» («Стоп во время простоя»).
/// </summary>
public sealed class IdeaCycleTests
{
    private static readonly DateTimeOffset Noon = IdeaGenerator.Noon;
    private static readonly TradeCosts Costs = TradeCosts.Default;

    [Test]
    public void The_entry_is_the_close_of_the_candle_until_the_first_minute_then_that_of_a_trade_from_its_open()
    {
        Idea idea = IdeaGenerator.Generate(close: 96180m);
        Assert.That(idea.ShownEntry, Is.EqualTo(96180.0));

        IdeaCycle.Minute(idea, Minute(Noon, first: 96200, high: 96250, low: 96150, close: 96210), Costs);

        Assert.That(idea.EntryRaw, Is.EqualTo(96200.0));
        Assert.That(idea.EntryPrice, Is.EqualTo(96200 * (1 - 0.0005)), "a short sells lower by the slippage, as a trade of a backtest");
        Assert.That((idea.ShownEntry, idea.EntryTime), Is.EqualTo((idea.EntryPrice!.Value, (DateTimeOffset?)Noon)));
    }

    [Test]
    public void The_stop_in_price_is_counted_from_the_entry()
    {
        Idea idea = IdeaGenerator.Generate(side: EntrySide.Long, stop: 0.02, target: 0.04, close: 236m);

        IdeaCycle.Minute(idea, Minute(Noon, first: 236.10m, high: 236.2m, low: 236m, close: 236.1m), Costs with { Slippage = 0 });

        Assert.That(idea.StopPrice, Is.EqualTo(231.378).Within(1e-9));
        Assert.That(idea.TargetPrice, Is.EqualTo(245.544).Within(1e-9));
    }

    [Test]
    public void An_idea_that_touched_neither_stop_nor_target_closes_at_the_end_of_its_time()
    {
        Idea idea = IdeaGenerator.Generate();
        for (int minute = 0; minute < 240; minute++)
        {
            Assert.That(IdeaCycle.Minute(idea, Minute(Noon.AddMinutes(minute), 100, 101, 99, 100 - (minute / 240m)), Costs), Is.False);
        }

        IdeaCycle.TimeUp(idea);
        IdeaCycle.Close(idea, Costs);

        Assert.That((idea.State, idea.ExitTime, idea.ExitReason, idea.ExitRaw), Is.EqualTo((IdeaState.Closed, (DateTimeOffset?)Noon.AddHours(4), ExitReason.Time, (double?)(double)(100 - (239 / 240m)))));
        double entry = 100 * (1 - 0.0005);
        double exit = (double)(100 - (239 / 240m)) * (1 + 0.0005);
        double traded = -(exit / entry - 1);
        Assert.That(idea.Result, Is.EqualTo(traded - (0.00055 * (1 + (exit / entry)))).Within(1e-15), "after the fee and the slippage of both sides");
        Assert.That(idea.ResultUsdt, Is.EqualTo(idea.Result * 100).Within(1e-12));
    }

    [Test]
    public void A_minute_through_the_stop_closes_the_idea_at_its_open_time_by_the_stop()
    {
        Idea idea = IdeaGenerator.Generate(side: EntrySide.Long, stop: 0.02, target: 0.05);
        IdeaCycle.Minute(idea, Minute(Noon, 100, 100, 100, 100), Costs);
        IdeaCycle.Minute(idea, Minute(Noon.AddMinutes(81), 100, 100.5m, 99, 99.5m), Costs);

        bool ended = IdeaCycle.Minute(idea, Minute(Noon.AddMinutes(82), 99, 99, 97, 97.5m), Costs);
        IdeaCycle.Close(idea, Costs);

        Assert.That(ended, Is.True);
        Assert.That((idea.ExitTime, idea.ExitReason, idea.Ambiguous), Is.EqualTo(((DateTimeOffset?)Noon.AddMinutes(82), ExitReason.Stop, false)), "13:22");
        Assert.That(idea.ExitRaw, Is.EqualTo(idea.StopPrice), "the stop below the open of the minute fills at its level");
        Assert.That(idea.Result, Is.LessThan(-0.02));
    }

    [Test]
    public void A_minute_through_the_stop_and_the_target_is_the_stop_and_ambiguous()
    {
        Idea idea = IdeaGenerator.Generate(side: EntrySide.Long, stop: 0.01, target: 0.01);
        IdeaCycle.Minute(idea, Minute(Noon, 100, 100, 100, 100), Costs);

        IdeaCycle.Minute(idea, Minute(Noon.AddMinutes(1), 100, 102, 98, 100), Costs);

        Assert.That((idea.ExitReason, idea.Ambiguous), Is.EqualTo((ExitReason.Stop, true)));
    }

    [Test]
    public void A_payment_at_the_moment_of_the_exit_counts_and_one_after_it_does_not()
    {
        Idea paid = IdeaGenerator.Generate(side: EntrySide.Short);
        Idea unpaid = IdeaGenerator.Generate(side: EntrySide.Short);
        foreach (Idea idea in new[] { paid, unpaid })
        {
            IdeaCycle.Minute(idea, Minute(Noon, 100, 100, 100, 100), Costs);
            IdeaCycle.Minute(idea, Minute(Noon.AddMinutes(239), 100, 100, 100, 100), Costs);
            IdeaCycle.TimeUp(idea);
        }

        IdeaCycle.Payment(paid, Noon.AddHours(4), 0.001, 100);
        IdeaCycle.Payment(unpaid, Noon.AddHours(4).AddMinutes(1), 0.001, 100);
        IdeaCycle.Close(paid, Costs);
        IdeaCycle.Close(unpaid, Costs);

        Assert.That(paid.FundingPaid, Is.EqualTo(0.1));
        Assert.That(unpaid.FundingPaid, Is.Zero);
        Assert.That(paid.Result - unpaid.Result, Is.EqualTo(0.1 / (100 * (1 - 0.0005))).Within(1e-12), "a short receives the payment of the longs");
    }

    [Test]
    public void A_short_in_the_red_has_a_result_now_below_zero()
    {
        Idea idea = IdeaGenerator.Generate(close: 96180m);
        IdeaCycle.Minute(idea, Minute(Noon, 96180, 96200, 96150, 96190), Costs);

        double? now = IdeaCycle.Now(idea, 96412.5, Noon.AddMinutes(40), Costs);

        Assert.That(now, Is.LessThan(0));
        Assert.That(IdeaCycle.Now(IdeaGenerator.Generate(), 96412.5, Noon, Costs), Is.Null, "no result before the entry");
    }

    [Test]
    public void An_idea_cut_off_from_its_data_is_excluded_with_the_reason_and_has_no_outcome()
    {
        Idea gap = IdeaGenerator.Generate();
        Idea delisted = IdeaGenerator.Generate();

        IdeaCycle.Exclude(gap, IdeaCycle.NoData);
        IdeaCycle.Exclude(delisted, IdeaCycle.Delisted);
        IdeaCycle.Close(gap, Costs);

        Assert.That((gap.State, gap.Excluded, gap.Result), Is.EqualTo((IdeaState.Excluded, (string?)"нет данных", (double?)null)));
        Assert.That((delisted.State, delisted.Excluded), Is.EqualTo((IdeaState.Excluded, (string?)"пара снята с торгов")));
    }

    [Test]
    public void After_a_restart_the_cycle_goes_on_from_the_row_and_finds_the_stop_of_the_downtime()
    {
        Idea before = IdeaGenerator.Generate(side: EntrySide.Long, stop: 0.02, after: TimeSpan.FromHours(4));
        for (int minute = 0; minute <= 60; minute++)
        {
            IdeaCycle.Minute(before, Minute(Noon.AddMinutes(minute), 100, 100.5m, 99.5m, 100), Costs);
        }

        Idea restored = Copy(before);
        for (int minute = 55; minute <= 70; minute++)
        {
            decimal low = minute == 64 ? 97 : 99.5m;
            IdeaCycle.Minute(restored, Minute(Noon.AddMinutes(minute), 100, 100.5m, low, 100), Costs);
        }

        Assert.That((restored.ExitTime, restored.ExitReason), Is.EqualTo(((DateTimeOffset?)Noon.AddMinutes(64), ExitReason.Stop)), "13:04, with the minutes caught up");
        Assert.That(restored.ComputedUntil, Is.EqualTo(Noon.AddMinutes(64)), "nothing after the exit");
    }

    [Test]
    public void An_idea_without_a_minute_with_trades_in_its_time_is_excluded()
    {
        Idea idea = IdeaGenerator.Generate();

        IdeaCycle.TimeUp(idea);

        Assert.That((idea.State, idea.Excluded), Is.EqualTo((IdeaState.Excluded, (string?)"нет сделок")));
    }

    // The idea as its row gives it back after a restart.
    private static Idea Copy(Idea idea)
    {
        return new Idea
        {
            Id = idea.Id,
            StrategyId = idea.StrategyId,
            StrategyName = idea.StrategyName,
            StrategyVersion = idea.StrategyVersion,
            Timeframe = idea.Timeframe,
            Symbol = idea.Symbol,
            Side = idea.Side,
            Moment = idea.Moment,
            Size = idea.Size,
            After = idea.After,
            Stop = idea.Stop,
            Target = idea.Target,
            CandleClose = idea.CandleClose,
            Labels = idea.Labels,
            EntryRaw = idea.EntryRaw,
            EntryPrice = idea.EntryPrice,
            EntryTime = idea.EntryTime,
            FundingPaid = idea.FundingPaid,
            LastClose = idea.LastClose,
            ComputedUntil = idea.ComputedUntil
        };
    }

    private static ClosedCandle Minute(DateTimeOffset open, decimal first, decimal high, decimal low, decimal close)
    {
        return new ClosedCandle(open, open.AddMinutes(1), first, high, low, close, 1);
    }
}
