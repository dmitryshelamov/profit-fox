using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
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

    // Scenario "Вход на ▲": the promise of the idea at the entry stays on the trade as it was given.
    // Decision 9 of add-market-idea-features: a trade copies the features of its idea when it is made.
    [Test]
    public void New_trade_copies_the_features_of_its_idea()
    {
        Idea idea = IdeaGenerator.Generate(Fired) with { PairChange = -0.08m, Funding = 0.0001m, BurstPairs = 30 };

        IdeaTrade.Create(Guid.NewGuid(), idea, Account, 106m, Size(), Now).Features.ShouldBe(new IdeaFeatures(true, false, false, true));
    }

    [Test]
    public void New_trade_carries_the_promise_it_is_given()
    {
        var promise = new IdeaPromise(IdeaBadge.Up, 0.31m, 0.14m, 412, 0.33m, 1.84m);

        var trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Fired), Account, 106m, Size(), Now, promise);

        trade.Promise.ShouldBe(promise);
        New().Promise.ShouldBeNull();
    }

    // Scenario "Сделка автомата": the auto that took the trade stays on it; a manual trade has none.
    [Test]
    public void New_trade_carries_the_auto_that_took_it()
    {
        var auto = Guid.NewGuid();

        IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Fired), Account, 106m, Size(), Now, autoId: auto).AutoId.ShouldBe(auto);
        New().AutoId.ShouldBeNull();
    }

    // Scenario "Символ с плечом 10×": the trade keeps the leverage it entered with.
    [Test]
    public void New_trade_on_linear_keeps_the_leverage_it_is_given()
    {
        Idea idea = IdeaGenerator.Generate(Fired, instrument: new Instrument(MarketCategory.Linear, "DKNGUSDT"));

        IdeaTrade.Create(Guid.NewGuid(), idea, Account, 106m, Size(), Now, leverage: 5).Leverage.ShouldBe(5);
    }

    // Scenarios "Вход на споте" and "Вход на MOEX": only linear has a leverage, whatever the settings say.
    [TestCase(MarketCategory.Spot)]
    [TestCase(MarketCategory.Moex)]
    public void New_trade_off_linear_keeps_the_leverage_of_one(MarketCategory category)
    {
        Idea idea = IdeaGenerator.Generate(Fired, instrument: new Instrument(category, "DKNGUSDT"));

        IdeaTrade.Create(Guid.NewGuid(), idea, Account, 106m, Size(), Now, leverage: 5).Leverage.ShouldBe(1);
    }

    // Scenario "Ждущая сделка занимает свой залог": a trade of 100 USDT at 4× takes 25 of the money of the account.
    [Test]
    public void Margin_is_the_amount_over_the_leverage_of_the_trade()
    {
        (New() with { Amount = 100m, Leverage = 4 }).Margin.ShouldBe(25m);
    }

    // Decision 8: a pullback waits the 4 candles of its window from the moment it fired; the others enter at once.
    [Test]
    public void Pullback_keeps_the_deadline_of_its_entry_and_the_price_of_its_idea()
    {
        Idea idea = IdeaGenerator.Generate(Fired, type: IdeaType.Pullback);

        var trade = IdeaTrade.Create(Guid.NewGuid(), idea, Account, 105m, Size(), Now);

        trade.EntryDeadline.ShouldBe(Fired.AddMinutes(60));
        trade.EntryExpired(Fired.AddMinutes(59)).ShouldBeFalse();
        trade.EntryExpired(Fired.AddMinutes(60)).ShouldBeTrue();
    }

    // tune-idea-rules task 2.4: the deadline follows the entry window of the idea, 8 candles of 15 minutes here.
    [Test]
    public void Pullback_waits_the_entry_window_of_its_idea()
    {
        Idea generated = IdeaGenerator.Generate(Fired, type: IdeaType.Pullback);
        Idea idea = generated with { Track = generated.Track with { EntryCandles = 8 } };

        IdeaTrade.Create(Guid.NewGuid(), idea, Account, 105m, Size(), Now).EntryDeadline.ShouldBe(Fired.AddMinutes(120));
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

    // Journal: the time in the trade runs from the first fill of the entry, and later fills and the exits do not move it.
    [Test]
    public void Trade_opens_at_the_first_fill_of_its_entry_and_keeps_that_time()
    {
        IdeaTrade waiting = New();
        IdeaTrade part = waiting.EntryGrown(new LegTotals(0.2m, 21.2m, 0.01m), Now.AddMinutes(1));
        IdeaTrade whole = part.EntryGrown(new LegTotals(0.47m, 49.82m, 0.02m), Now.AddMinutes(2));
        IdeaTrade closed = whole.ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.47m, 55m, 0.02m), Now.AddMinutes(3));

        waiting.OpenedAt.ShouldBeNull();
        (part.OpenedAt, whole.OpenedAt, closed.OpenedAt).ShouldBe((Now.AddMinutes(1), Now.AddMinutes(1), Now.AddMinutes(1)));
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

    // Requirement "Закрытие учебной сделки по сроку идеи": the expiry leg sold 0.47 at 23.83 with the fee 0.0062.
    [Test]
    public void Exit_by_the_expiry_leg_closes_the_trade_at_expiry_with_the_result()
    {
        IdeaTrade trade = Entered().ExitGrown(IdeaTradeLeg.Expiry, new LegTotals(0.47m, 11.2001m, 0.0062m), Now.AddMinutes(5));

        (trade.Status, trade.StatusAt, trade.Expiry, trade.HeldQuantity).ShouldBe((IdeaTradeStatus.ClosedAtExpiry, Now.AddMinutes(5), new LegTotals(0.47m, 11.2001m, 0.0062m), 0m));
        trade.Result.ShouldBe(11.2001m - 11.0356m - 0.0061m - 0.0062m);
        trade.Fees.ShouldBe(0.0061m + 0.0062m);
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

    // The exits of a leg are told one by one (a second market close, a stop and a target for each part of a pullback): they add up.
    [Test]
    public void Exits_of_one_leg_add_up_and_the_last_one_closes_the_trade()
    {
        IdeaTrade part = Entered().ExitGrown(IdeaTradeLeg.Manual, new LegTotals(0.2m, 4.7m, 0.002m), Now.AddMinutes(5));

        IdeaTrade closed = part.ExitGrown(IdeaTradeLeg.Manual, new LegTotals(0.27m, 6.4m, 0.003m), Now.AddMinutes(6));

        (part.Status, part.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.27m));
        (closed.Status, closed.Manual).ShouldBe((IdeaTradeStatus.ClosedManually, new LegTotals(0.47m, 11.1m, 0.005m)));
    }

    // The exchange may tell of the stop before the books count the entry: the entry that comes after finds nothing held and closes the trade.
    [Test]
    public void Entry_counted_after_the_exit_that_took_all_of_it_closes_the_trade()
    {
        IdeaTrade trade = New().ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(0.47m, 10.8147m, 0.0059m), Now.AddMinutes(5));

        IdeaTrade closed = trade.EntryGrown(new LegTotals(0.47m, 11.0356m, 0.0061m), Now.AddMinutes(6));

        (closed.Status, closed.StatusAt).ShouldBe((IdeaTradeStatus.ClosedAtStop, Now.AddMinutes(6)));
        closed.Result.ShouldBe(10.8147m - 11.0356m - 0.0061m - 0.0059m);
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

    // Scenario "Открытая в плюсе": long 0.47 at 23.48 (11.0356), the price 23.90 (11.233), taker 0.055 % on the exit, the fee of the entry 0.0061.
    [Test]
    public void Open_trade_is_worth_the_rest_closed_at_the_price_with_the_taker_fee_of_the_exit()
    {
        IdeaTrade trade = Entered();

        trade.ResultAt(23.90m, 0.055m).ShouldBe(11.233m - 11.0356m - 0.0061m - (11.233m * 0.00055m));
        trade.ResultRAt(23.90m, 0.055m).ShouldBe(trade.ResultAt(23.90m, 0.055m) / 0.2m);
    }

    [Test]
    public void Open_trade_that_took_a_part_out_adds_the_exit_it_made_to_the_rest()
    {
        IdeaTrade trade = Entered().ExitGrown(IdeaTradeLeg.TakeProfit, new LegTotals(0.2m, 4.884m, 0.0027m), Now.AddMinutes(5));

        trade.ResultAt(23.90m, 0.055m).ShouldBe(4.884m + (0.27m * 23.90m) - 11.0356m - 0.0061m - 0.0027m - (0.27m * 23.90m * 0.00055m));
    }

    [Test]
    public void Short_trade_that_is_open_earns_when_the_price_is_lower()
    {
        IdeaTrade trade = IdeaTrade.Create(Guid.NewGuid(), IdeaGenerator.Generate(Fired, side: IdeaSide.Short), Account, 105m, Size(), Now)
            .EntryGrown(new LegTotals(0.47m, 49.35m, 0.03m), Now);

        trade.ResultAt(100m, 0.055m).ShouldBe(49.35m - 47m - 0.03m - (47m * 0.00055m));
    }

    [Test]
    public void Trade_that_waits_for_its_entry_has_no_current_result()
    {
        New().ResultAt(23.90m, 0.055m).ShouldBeNull();
        New().ResultRAt(23.90m, 0.055m).ShouldBeNull();
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
        return new IdeaTradeSize(0.47m, 50m, 0.2m, 0.368m, 1.84m);
    }
}