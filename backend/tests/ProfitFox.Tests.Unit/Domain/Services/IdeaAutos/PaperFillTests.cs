using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Services.IdeaAutos;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Fixtures.IdeaAutos;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.IdeaAutos;

/// <summary>
///     Requirements "Вход учебной сделки", "Выход учебной сделки" and "Комиссии и итог учебной сделки" on the reference minutes of
///     <see cref="IdeaAutoFixtures" />: 50 USDT at 100 with the stop at 98 is 0.5 with a risk of 1.05445 (1.03695 for a limit, which pays the
///     maker fee of 0.02 %), the taker fee is 0.055 %.
/// </summary>
public class PaperFillTests
{
    private static readonly FeeRates Fees = new FeeRates(0.02m, 0.055m);
    private static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.01m, 0.01m, 5m);
    private static readonly DateTimeOffset Fired = new(2026, 11, 7, 13, 40, 0, TimeSpan.Zero);
    private static readonly Guid Auto = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");

    // Scenario "Рыночный тип": the idea was born in the minute that closed at 100.
    [Test]
    public void Market_idea_opens_at_the_close_of_the_minute_it_was_born_in()
    {
        PaperIdeaTrade paper = Open("paper-to-target", out _);
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.EntryPrice, trade.Stop, trade.Target).ShouldBe((IdeaTradeStatus.Open, 100m, 98m, 104m));
        (trade.Quantity, trade.Amount, trade.Risk).ShouldBe((0.5m, 50m, 1.05445m));
        trade.Entry.ShouldBe(new LegTotals(0.5m, 50m, 0.0275m));
        (trade.AccountId, trade.AutoId, paper.AutoId).ShouldBe((Account, Auto, Auto));
    }

    // The time of the opening is the end of the minute of the birth, and the trade is stepped from the next minute on.
    [Test]
    public void Market_trade_opens_at_the_end_of_the_birth_minute_and_is_stepped_from_the_next_one()
    {
        PaperIdeaTrade paper = Open("paper-to-target", out _);

        (paper.Trade.OpenedAt, paper.Trade.StatusAt, paper.Trade.CreatedAt, paper.SteppedUntil).ShouldBe((Fired.AddMinutes(1), Fired.AddMinutes(1), Fired.AddMinutes(1), Fired.AddMinutes(1)));
        paper.Trade.EntryDeadline.ShouldBeNull();
    }

    [Test]
    public void Paper_trade_keeps_the_promise_it_is_given()
    {
        var promise = new IdeaPromise(IdeaBadge.Flat, 0.3m, 0.01m, 120, 0.35m, 1.8m);

        Open("paper-to-target", out _, promise: promise).Trade.Promise.ShouldBe(promise);
    }

    // Scenario "Цель": 2 − 0.0275 − 0.0286 = 1.9439, and about +1.84 R.
    [Test]
    public void Target_closes_at_the_price_of_the_target_with_the_money_of_the_scenario()
    {
        PaperIdeaTrade paper = Run("paper-to-target");
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.StatusAt).ShouldBe((IdeaTradeStatus.ClosedAtTarget, Fired.AddMinutes(3)));
        trade.TakeProfit.ShouldBe(new LegTotals(0.5m, 52m, 0.0286m));
        trade.Result.ShouldBe(1.9439m);
        Math.Round(trade.ResultR!.Value, 2).ShouldBe(1.84m);
        trade.TimeToOutcome.ShouldBe(TimeSpan.FromMinutes(2));
    }

    // Scenario "Стоп": −1.05445 USDT and −1 R.
    [Test]
    public void Stop_closes_at_the_price_of_the_stop_with_the_money_of_the_scenario()
    {
        PaperIdeaTrade paper = Run("paper-to-stop");
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.StatusAt).ShouldBe((IdeaTradeStatus.ClosedAtStop, Fired.AddMinutes(2)));
        trade.StopLoss.ShouldBe(new LegTotals(0.5m, 49m, 0.02695m));
        (trade.Result, trade.ResultR).ShouldBe((-1.05445m, -1m));
    }

    // Scenario "Оба в одной свече": the minute runs from 97.9 to 104.1.
    [Test]
    public void Minute_that_touches_both_closes_at_the_stop()
    {
        PaperIdeaTrade paper = Run("paper-both");

        (paper.Trade.Status, paper.Trade.StopLoss.Value, paper.Trade.TakeProfit.Value).ShouldBe((IdeaTradeStatus.ClosedAtStop, 49m, 0m));
    }

    [Test]
    public void Short_closes_at_the_target_below_the_entry()
    {
        PaperIdeaTrade paper = Run("paper-short-to-target", side: IdeaSide.Short);
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.Result).ShouldBe((IdeaTradeStatus.ClosedAtTarget, 1.9461m));
    }

    // Scenario "Идея истекла, сделка нет": the lifetime of the idea passed (24 candles of 5 minutes) and the trade does not know it.
    [Test]
    public void Trade_has_no_lifetime_and_stays_open_between_its_levels()
    {
        PaperIdeaTrade paper = Run("paper-quiet");

        (paper.Trade.Status, paper.SteppedUntil).ShouldBe((IdeaTradeStatus.Open, new DateTimeOffset(2026, 11, 7, 16, 1, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Minute_that_was_stepped_already_changes_nothing()
    {
        PaperIdeaTrade paper = Run("paper-to-target", until: 2);

        PaperFill.Step(paper, IdeaAutoFixtures.Minutes("paper-to-target")[2], Fees).ShouldBe(paper);
    }

    [Test]
    public void Trade_that_is_done_is_not_stepped_again()
    {
        PaperIdeaTrade paper = Run("paper-to-stop");
        var next = new IdeaCandle(Fired.AddMinutes(10), 100m, 104.1m, 97.9m, 100m, 1m);

        PaperFill.Step(paper, next, Fees).ShouldBe(paper);
    }

    [Test]
    public void Stepped_minute_moves_the_cursor_to_the_next_one()
    {
        PaperIdeaTrade paper = Run("paper-to-target", until: 1);

        (paper.Trade.Status, paper.SteppedUntil).ShouldBe((IdeaTradeStatus.Open, Fired.AddMinutes(2)));
    }

    // Scenario "Откат коснулся": the limit at 100 waits, a minute with a low of 99.9 touches it.
    [Test]
    public void Pullback_waits_for_its_limit_at_the_price_of_its_idea()
    {
        PaperIdeaTrade paper = Open("paper-pullback-touch", out _, IdeaType.Pullback);
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.EntryPrice, trade.OpenedAt, trade.Entry).ShouldBe((IdeaTradeStatus.AwaitingEntry, 100m, null, LegTotals.None));
        (trade.Risk, trade.EntryDeadline, paper.SteppedUntil).ShouldBe((1.03695m, Fired.AddMinutes(20), Fired.AddMinutes(1)));
    }

    [Test]
    public void Pullback_enters_at_its_price_in_the_first_minute_that_touches_it()
    {
        PaperIdeaTrade paper = Run("paper-pullback-touch", IdeaType.Pullback, until: 2);
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.OpenedAt, trade.StatusAt).ShouldBe((IdeaTradeStatus.Open, Fired.AddMinutes(2), Fired.AddMinutes(2)));
        trade.Entry.ShouldBe(new LegTotals(0.5m, 50m, 0.01m));
    }

    [Test]
    public void Pullback_that_is_not_touched_keeps_waiting()
    {
        PaperIdeaTrade paper = Run("paper-pullback-touch", IdeaType.Pullback, until: 1);

        (paper.Trade.Status, paper.Trade.Entry, paper.SteppedUntil).ShouldBe((IdeaTradeStatus.AwaitingEntry, LegTotals.None, Fired.AddMinutes(2)));
    }

    [Test]
    public void Pullback_pays_the_maker_fee_at_the_entry_and_the_taker_fee_at_the_exit()
    {
        PaperIdeaTrade paper = Run("paper-pullback-touch", IdeaType.Pullback);

        (paper.Trade.Status, paper.Trade.Result).ShouldBe((IdeaTradeStatus.ClosedAtTarget, 1.9614m));
    }

    // Scenario "Цель в минуту входа Отката": the minute touched the limit and the target but not the stop.
    [Test]
    public void Target_in_the_minute_of_the_pullback_entry_does_not_count()
    {
        PaperIdeaTrade entered = Run("paper-pullback-and-target", IdeaType.Pullback, until: 1);

        (entered.Trade.Status, entered.Trade.TakeProfit).ShouldBe((IdeaTradeStatus.Open, LegTotals.None));
        Run("paper-pullback-and-target", IdeaType.Pullback).Trade.StatusAt.ShouldBe(Fired.AddMinutes(2));
    }

    [Test]
    public void Stop_in_the_minute_of_the_pullback_entry_counts()
    {
        PaperIdeaTrade paper = Run("paper-pullback-and-stop", IdeaType.Pullback);

        (paper.Trade.Status, paper.Trade.OpenedAt, paper.Trade.StatusAt, paper.Trade.Result).ShouldBe((IdeaTradeStatus.ClosedAtStop, Fired.AddMinutes(1), Fired.AddMinutes(1), -1.03695m));
    }

    // Scenario "Откат не дождался": no minute before the deadline reached the limit; the minute that opens at the deadline is too late.
    [Test]
    public void Pullback_not_touched_by_its_deadline_is_cancelled_without_a_result()
    {
        PaperIdeaTrade paper = Run("paper-pullback-missed", IdeaType.Pullback, entryCandles: 1);
        IdeaTrade trade = paper.Trade;

        (trade.Status, trade.StatusAt, trade.Result, trade.OpenedAt).ShouldBe((IdeaTradeStatus.Cancelled, Fired.AddMinutes(5), null, null));
    }

    private static PaperIdeaTrade Run(string fixture, IdeaType type = IdeaType.TwoR, IdeaSide side = IdeaSide.Long, int? until = null, int? entryCandles = null)
    {
        PaperIdeaTrade paper = Open(fixture, out IReadOnlyList<IdeaCandle> minutes, type, side, entryCandles: entryCandles);

        foreach (IdeaCandle minute in minutes.Skip(1).Take(until ?? minutes.Count))
        {
            paper = PaperFill.Step(paper, minute, Fees);
        }

        return paper;
    }

    private static PaperIdeaTrade Open(string fixture, out IReadOnlyList<IdeaCandle> minutes, IdeaType type = IdeaType.TwoR, IdeaSide side = IdeaSide.Long, IdeaPromise? promise = null, int? entryCandles = null)
    {
        minutes = IdeaAutoFixtures.Minutes(fixture);
        Idea generated = IdeaGenerator.Generate(Fired, type: type, side: side);
        IdeaPlan plan = side == IdeaSide.Long ? new IdeaPlan(type, side, 100m, 98m, 104m) : new IdeaPlan(type, side, 100m, 102m, 96m);
        Idea idea = generated with { Track = generated.Track with { Plan = plan, CandleMinutes = 5, EntryCandles = entryCandles ?? generated.Track.EntryCandles } };
        decimal price = PaperFill.EntryPrice(idea, minutes[0]);
        IdeaTradeSize size = IdeaTradeSizing.Calculate(plan with { Entry = price }, Fees, 50m, Rules);

        return PaperFill.Open(Guid.NewGuid(), idea, Auto, Account, size, promise, minutes[0], Fees);
    }
}