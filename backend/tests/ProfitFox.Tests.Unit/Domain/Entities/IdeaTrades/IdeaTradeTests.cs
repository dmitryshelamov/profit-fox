using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.IdeaTrades;

public class IdeaTradeTests
{
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Fired = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Fired.AddMinutes(1);

    [Test]
    public void New_trade_waits_for_its_entry_with_the_levels_of_the_idea()
    {
        Idea idea = IdeaGenerator.Generate(Fired);

        var trade = IdeaTrade.Create(Guid.NewGuid(), idea, Account, 106m, Size(), Now);

        (trade.IdeaId, trade.AccountId, trade.Instrument, trade.Setup, trade.Interval, trade.Type, trade.Side).ShouldBe((idea.Id, Account, idea.Instrument, idea.Setup, idea.Interval, IdeaType.TwoR, IdeaSide.Long));
        (trade.EntryPrice, trade.Stop, trade.Target).ShouldBe((106m, 100m, 115m));
        (trade.Quantity, trade.Amount, trade.Risk).ShouldBe((0.47m, 50m, 0.2m));
        (trade.Status, trade.StatusAt, trade.Reason, trade.CreatedAt).ShouldBe((IdeaTradeStatus.AwaitingEntry, Now, null, Now));
        trade.IsOpen.ShouldBeTrue();
        trade.Result.ShouldBeNull();
    }

    // Decision 8: a pullback waits 4 candles of its interval from the moment it fired; the others enter at once.
    [Test]
    public void Pullback_keeps_the_deadline_of_its_entry_and_the_price_of_its_idea()
    {
        Idea idea = IdeaGenerator.Generate(Fired, type: IdeaType.Pullback);

        var trade = IdeaTrade.Create(Guid.NewGuid(), idea, Account, 105m, Size(), Now);

        trade.EntryDeadline.ShouldBe(Fired.AddMinutes(60));
        trade.EntryExpired(Fired.AddMinutes(59)).ShouldBeFalse();
        trade.EntryExpired(Fired.AddMinutes(60)).ShouldBeTrue();
    }

    [Test]
    public void Market_trade_has_no_deadline()
    {
        IdeaTrade trade = New();

        trade.EntryDeadline.ShouldBeNull();
        trade.EntryExpired(Fired.AddDays(1)).ShouldBeFalse();
    }

    [Test]
    public void Entry_filled_in_part_opens_the_trade_with_the_filled_quantity()
    {
        IdeaTrade trade = New().EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now.AddMinutes(1));

        (trade.Status, trade.StatusAt, trade.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, Now.AddMinutes(1), 0.2m));
    }

    [Test]
    public void Entry_filled_whole_opens_the_trade_with_the_whole_quantity()
    {
        IdeaTrade trade = New().EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now).EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now.AddMinutes(1));

        (trade.Status, trade.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.47m));
        trade.StatusAt.ShouldBe(Now, "the trade opened with the first fill");
    }

    [Test]
    public void Entry_that_ends_with_no_fill_cancels_the_trade_without_a_result()
    {
        IdeaTrade trade = New().EntryEnded(Now.AddMinutes(1));

        (trade.Status, trade.StatusAt, trade.IsOpen, trade.Result).ShouldBe((IdeaTradeStatus.Cancelled, Now.AddMinutes(1), false, null));
    }

    [Test]
    public void Entry_that_ends_after_a_fill_leaves_the_trade_open()
    {
        IdeaTrade trade = New().EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now).EntryEnded(Now.AddMinutes(1));

        (trade.Status, trade.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.2m));
    }

    [Test]
    public void Cancel_is_asked_only_of_a_trade_that_waits_for_its_entry()
    {
        New().RequestCancel().CancelRequested.ShouldBeTrue();
        Should.Throw<IdeaTradeConflictException>(() => New().EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now).RequestCancel());
    }

    [Test]
    public void Close_is_asked_only_of_an_open_trade()
    {
        New().EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now).RequestClose().CloseRequested.ShouldBeTrue();
        Should.Throw<IdeaTradeConflictException>(() => New().RequestClose());
    }

    // Scenario "Цель": bought 0.47 at 23.48, sold 0.47 at 24.42, the fees 0.0061 and 0.0063.
    [Test]
    public void Target_closes_the_trade_with_the_result_in_money_and_in_r()
    {
        IdeaTrade trade = Entered().ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 11.4774m, 0.0063m), Now.AddMinutes(5));

        (trade.Status, trade.StatusAt, trade.IsOpen).ShouldBe((IdeaTradeStatus.ClosedAtTarget, Now.AddMinutes(5), false));
        trade.Result.ShouldBe(11.4774m - 11.0356m - 0.0061m - 0.0063m);
        trade.ResultR.ShouldBe(0.4294m / 0.2m);
    }

    [Test]
    public void Stop_closes_the_trade_with_a_loss()
    {
        IdeaTrade trade = Entered().ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(0.47m, 10.8147m, 0.0059m), Now.AddMinutes(5));

        trade.Status.ShouldBe(IdeaTradeStatus.ClosedAtStop);
        trade.Result.ShouldBe(10.8147m - 11.0356m - 0.0061m - 0.0059m);
    }

    [Test]
    public void Market_close_of_the_owner_closes_the_trade_manually()
    {
        Entered().ExitGrown(IdeaTradeLeg.Manual, new LegTotals(0.47m, 11.0000m, 0.0060m), Now.AddMinutes(5)).Status.ShouldBe(IdeaTradeStatus.ClosedManually);
    }

    [Test]
    public void Exit_of_a_part_leaves_the_trade_open_with_the_rest()
    {
        IdeaTrade trade = Entered().ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.2m, 4.884m, 0.0027m), Now.AddMinutes(5));

        (trade.Status, trade.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.27m));
        trade.Result.ShouldBeNull();
    }

    // The exchange may tell of the stop before the books count the entry: nothing is held yet, and nothing is closed.
    [Test]
    public void Exit_counted_before_the_entry_does_not_close_the_trade()
    {
        IdeaTrade trade = New().ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(0.47m, 10.8147m, 0.0059m), Now.AddMinutes(5));

        trade.Status.ShouldBe(IdeaTradeStatus.AwaitingEntry);
    }

    // The other leg is cancelled when one fires, and its end comes after: it took out nothing and closes nothing a second time.
    [Test]
    public void Leg_that_took_nothing_does_not_change_the_close_of_a_closed_trade()
    {
        IdeaTrade closed = Entered().ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 11.4774m, 0.0063m), Now.AddMinutes(5));

        IdeaTrade later = closed.ExitGrown(IdeaTradeLeg.StopLoss, LegTotals.None, Now.AddMinutes(6));

        later.ShouldBe(closed);
    }

    [Test]
    public void Short_trade_earns_when_it_buys_back_lower()
    {
        Idea idea = IdeaGenerator.Generate(Fired, side: IdeaSide.Short);
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), idea, Account, 105m, Size(), Now)
            .EntryGrown(new LegTotals(0.47m, 49.35m, 0.03m), Now)
            .ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 44.65m, 0.03m), Now.AddMinutes(5));

        trade.Result.ShouldBe(49.35m - 44.65m - 0.03m - 0.03m);
    }

    [Test]
    public void Failure_stops_an_open_trade_with_the_first_reason()
    {
        IdeaTrade trade = Entered().Fail("position 0 does not match", Now.AddMinutes(2)).Fail("another reason", Now.AddMinutes(3));

        (trade.Status, trade.Reason, trade.StatusAt, trade.IsOpen).ShouldBe((IdeaTradeStatus.Failed, "position 0 does not match", Now.AddMinutes(2), false));
        trade.Result.ShouldBeNull();
    }

    [Test]
    public void Failure_stops_a_trade_that_waits_for_its_entry()
    {
        New().Fail("Bybit refused the order", Now).Status.ShouldBe(IdeaTradeStatus.Failed);
    }

    [Test]
    public void Closed_trade_does_not_fail_afterwards()
    {
        IdeaTrade closed = Entered().ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 11.4774m, 0.0063m), Now.AddMinutes(5));

        closed.Fail("late", Now.AddMinutes(6)).ShouldBe(closed);
    }

    private static IdeaTrade New()
    {
        return IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Fired), Account, 106m, Size(), Now);
    }

    private static IdeaTrade Entered()
    {
        return New().EntryGrown(new LegTotals(0.47m, 11.0356m, 0.0061m), Now);
    }

    private static IdeaTradeSize Size()
    {
        return new IdeaTradeSize(0.47m, 50m, 0.2m, 1.84m, false);
    }
}