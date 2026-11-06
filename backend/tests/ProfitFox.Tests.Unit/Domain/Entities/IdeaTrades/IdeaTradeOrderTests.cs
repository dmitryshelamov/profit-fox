using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.IdeaTrades;

/// <summary>An order of a trade is saved before it goes to the exchange and moves like an order of a bot.</summary>
public class IdeaTradeOrderTests
{
    private static readonly Guid Trade = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void New_entry_waits_to_be_sent_with_its_stop_and_target()
    {
        var order = IdeaTradeOrder.NewEntry(Trade, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GoodTilCancelled, 106m, 0.47m, new Protection(115m, 100m), Now);

        (order.TradeId, order.Number, order.Role, order.Side, order.Type, order.TimeInForce).ShouldBe((Trade, 1, IdeaTradeOrderRole.Entry, OrderSide.Buy, OrderType.Limit, TimeInForce.GoodTilCancelled));
        (order.Price, order.Quantity, order.Protection, order.ReduceOnly).ShouldBe((106m, 0.47m, new Protection(115m, 100m), false));
        (order.Status, order.FilledQuantity, order.FilledValue, order.Fee, order.Attempts, order.NextAttemptAt).ShouldBe((LiveOrderStatus.PendingSubmit, 0m, 0m, 0m, 0, null));
        order.ClientId.ShouldBe(new IdeaTradeOrderId(Trade, 1));
    }

    [Test]
    public void New_close_is_a_market_order_that_only_reduces_the_position()
    {
        var order = IdeaTradeOrder.NewClose(Trade, 2, OrderSide.Sell, 0.47m, Now);

        (order.Role, order.Side, order.Type, order.TimeInForce, order.Price, order.Quantity, order.Protection, order.ReduceOnly)
            .ShouldBe((IdeaTradeOrderRole.Close, OrderSide.Sell, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, null, true));
    }

    // Requirement "Вход в сделку": a pullback enters with a limit at the price of its idea, the others by the market on linear and with a limit IOC at the current price on the spot.
    [TestCase(IdeaType.Pullback, MarketCategory.Linear, IdeaSide.Long, OrderSide.Buy, OrderType.Limit, TimeInForce.GoodTilCancelled, 106)]
    [TestCase(IdeaType.Pullback, MarketCategory.Spot, IdeaSide.Long, OrderSide.Buy, OrderType.Limit, TimeInForce.GoodTilCancelled, 106)]
    [TestCase(IdeaType.TwoR, MarketCategory.Linear, IdeaSide.Long, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null)]
    [TestCase(IdeaType.TwoR, MarketCategory.Linear, IdeaSide.Short, OrderSide.Sell, OrderType.Market, TimeInForce.GoodTilCancelled, null)]
    [TestCase(IdeaType.Levels, MarketCategory.Spot, IdeaSide.Long, OrderSide.Buy, OrderType.Limit, TimeInForce.ImmediateOrCancel, 106)]
    public void Entry_of_a_trade_is_made_by_the_type_of_its_idea_and_its_market(
        IdeaType type,
        MarketCategory category,
        IdeaSide side,
        OrderSide orderSide,
        OrderType orderType,
        TimeInForce timeInForce,
        decimal? price)
    {
        Idea idea = IdeaGenerator.Generate(Now, type: type, side: side, instrument: new Instrument(category, "BTCUSDT"));
        var trade = IdeaTrade.Create(Trade, idea, Guid.NewGuid(), 106m, new IdeaTradeSize(0.47m, 49.82m, 0.2m, 0.368m, 1.84m), Now);

        var order = IdeaTradeOrder.NewEntryOf(trade, Now);

        (order.TradeId, order.Number, order.Role, order.Side, order.Type, order.TimeInForce, order.Price, order.Quantity, order.Protection, order.ReduceOnly)
            .ShouldBe((Trade, 1, IdeaTradeOrderRole.Entry, orderSide, orderType, timeInForce, price, 0.47m, new Protection(trade.Target, trade.Stop), false));
    }

    [Test]
    public void Order_waiting_to_be_sent_is_taken_by_the_exchange()
    {
        IdeaTradeOrder taken = Entry().Taken("b-1");

        (taken.Status, taken.ExchangeOrderId).ShouldBe((LiveOrderStatus.Active, "b-1"));
    }

    [TestCase(LiveOrderStatus.PendingCancel)]
    [TestCase(LiveOrderStatus.CancelSent)]
    public void Order_already_sent_keeps_its_status_when_taken(LiveOrderStatus status)
    {
        IdeaTradeOrder taken = Entry() with { Status = status };

        taken.Taken("b-1").Status.ShouldBe(status);
        taken.Taken(null).ExchangeOrderId.ShouldBeNull();
    }

    [Test]
    public void Growth_keeps_the_totals_and_a_filled_order_is_filled()
    {
        IdeaTradeOrder grown = Entry().Taken("b-1").Grown(0.2m, 21.2m, 0.01m, filled: false);
        IdeaTradeOrder filled = grown.Grown(0.47m, 49.82m, 0.02m, filled: true);

        (grown.FilledQuantity, grown.FilledValue, grown.Fee, grown.Status).ShouldBe((0.2m, 21.2m, 0.01m, LiveOrderStatus.Active));
        (filled.FilledQuantity, filled.Status).ShouldBe((0.47m, LiveOrderStatus.Filled));
    }

    [Test]
    public void Cancel_is_saved_to_be_sent_at_once()
    {
        IdeaTradeOrder cancel = (Entry() with { Attempts = 2, NextAttemptAt = Now.AddMinutes(1) }).RequestCancel(Now.AddSeconds(5));

        (cancel.Status, cancel.NextAttemptAt, cancel.UpdatedAt, cancel.Cancelling).ShouldBe((LiveOrderStatus.PendingCancel, null, Now.AddSeconds(5), true));
        cancel.CancelTaken().Status.ShouldBe(LiveOrderStatus.CancelSent);
    }

    [Test]
    public void Refused_send_is_tried_again_ever_more_slowly_up_to_a_minute()
    {
        IdeaTradeOrder first = Entry().Deferred(Now);
        IdeaTradeOrder far = (Entry() with { Attempts = 9 }).Deferred(Now);

        (first.Attempts, first.NextAttemptAt).ShouldBe((1, Now.AddSeconds(1)));
        far.NextAttemptAt.ShouldBe(Now.AddSeconds(60));
        first.DueAt(Now).ShouldBeFalse();
        first.DueAt(Now.AddSeconds(1)).ShouldBeTrue();
    }

    [Test]
    public void Only_a_saved_send_or_cancel_is_due()
    {
        Entry().DueAt(Now).ShouldBeTrue();
        Entry().RequestCancel(Now).DueAt(Now).ShouldBeTrue();
        Entry().Taken("b-1").DueAt(Now).ShouldBeFalse();
    }

    [Test]
    public void Order_ends_cancelled_rejected_or_filled()
    {
        Entry().Cancelled().Status.ShouldBe(LiveOrderStatus.Cancelled);
        Entry().Completed().Status.ShouldBe(LiveOrderStatus.Filled);

        IdeaTradeOrder rejected = Entry().Rejected("Bybit refused");

        (rejected.Status, rejected.RejectReason).ShouldBe((LiveOrderStatus.Rejected, "Bybit refused"));
        rejected.Unfinished.ShouldBeFalse();
        Entry().Unfinished.ShouldBeTrue();
    }

    private static IdeaTradeOrder Entry()
    {
        return IdeaTradeOrder.NewEntry(Trade, 1, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, new Protection(115m, 100m), Now);
    }
}