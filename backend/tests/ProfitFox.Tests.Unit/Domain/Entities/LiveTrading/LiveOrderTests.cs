using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.LiveTrading;

public class LiveOrderTests
{
    private static readonly Guid Bot = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    // A new order is saved before it goes to the exchange: nothing of it is filled, sent or tried yet.
    [Test]
    public void New_order_waits_to_be_sent()
    {
        var order = LiveOrder.New(Bot, 7, 2, "entry", OrderSide.Buy, OrderType.Limit, 100m, 0.5m, null, 50m, Now);

        (order.BotId, order.Number, order.PositionNumber, order.NodePath, order.Side, order.Type).ShouldBe((Bot, 7, 2, "entry", OrderSide.Buy, OrderType.Limit));
        (order.Price, order.Quantity, order.Amount, order.Reserve).ShouldBe((100m, 0.5m, null, 50m));
        (order.Status, order.FilledQuantity, order.FilledValue, order.Fee).ShouldBe((LiveOrderStatus.PendingSubmit, 0m, 0m, 0m));
        (order.ExchangeOrderId, order.RejectReason, order.Attempts, order.NextAttemptAt).ShouldBe((null, null, 0, null));
        (order.CreatedAt, order.UpdatedAt).ShouldBe((Now, Now));
    }

    // The answer to the send and the order the exchange reports both take an order waiting to be sent.
    [Test]
    public void Order_waiting_to_be_sent_is_taken_by_the_exchange()
    {
        LiveOrder taken = Order().Taken("b-1");

        (taken.Status, taken.ExchangeOrderId).ShouldBe((LiveOrderStatus.Active, "b-1"));
    }

    // A cancel saved before the exchange told it took the order stays saved; the order only learns its exchange id.
    [TestCase(LiveOrderStatus.Active)]
    [TestCase(LiveOrderStatus.PendingCancel)]
    [TestCase(LiveOrderStatus.CancelSent)]
    public void Order_already_sent_keeps_its_status_when_taken(LiveOrderStatus status)
    {
        LiveOrder taken = Order(status).Taken("b-1");

        (taken.Status, taken.ExchangeOrderId).ShouldBe((status, "b-1"));
    }

    // Bybit answers a repeated client id without the exchange id: the id the order has stays.
    [Test]
    public void Taken_without_an_exchange_id_keeps_the_one_the_order_has()
    {
        LiveOrder order = Order() with { ExchangeOrderId = "b-1" };

        order.Taken(null).ExchangeOrderId.ShouldBe("b-1");
    }

    // The totals are what the exchange counted so far; a part of the order filled leaves it working.
    [TestCase(LiveOrderStatus.Active)]
    [TestCase(LiveOrderStatus.PendingCancel)]
    public void Part_filled_order_takes_the_totals_and_keeps_its_status(LiveOrderStatus status)
    {
        LiveOrder grown = Order(status).Grown(0.2m, 20.1m, 0.02m, false);

        (grown.Status, grown.FilledQuantity, grown.FilledValue, grown.Fee).ShouldBe((status, 0.2m, 20.1m, 0.02m));
    }

    [Test]
    public void Order_the_exchange_filled_is_filled()
    {
        LiveOrder grown = Order(LiveOrderStatus.Active).Grown(0.5m, 50.2m, 0.05m, true);

        (grown.Status, grown.FilledQuantity, grown.FilledValue, grown.Fee).ShouldBe((LiveOrderStatus.Filled, 0.5m, 50.2m, 0.05m));
    }

    // A cancel is saved before it goes to the exchange and goes at once, whatever pause the order waited for.
    [Test]
    public void Cancel_is_saved_to_be_sent_now()
    {
        LiveOrder order = Order(LiveOrderStatus.Active) with { NextAttemptAt = Now.AddSeconds(8) };

        LiveOrder cancelling = order.RequestCancel(Now.AddSeconds(1));

        (cancelling.Status, cancelling.NextAttemptAt, cancelling.UpdatedAt).ShouldBe((LiveOrderStatus.PendingCancel, null, Now.AddSeconds(1)));
    }

    [Test]
    public void Cancel_the_exchange_took_waits_for_its_confirmation()
    {
        Order(LiveOrderStatus.PendingCancel).CancelTaken().Status.ShouldBe(LiveOrderStatus.CancelSent);
    }

    // A send the exchange did not take is tried again ever more slowly: 1, 2, 4… seconds, at most a minute.
    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(3, 8)]
    [TestCase(5, 32)]
    [TestCase(6, 60)]
    [TestCase(10, 60)]
    public void Send_is_deferred_ever_more_slowly(int attempts, int seconds)
    {
        LiveOrder deferred = (Order() with { Attempts = attempts }).Deferred(Now);

        (deferred.Attempts, deferred.NextAttemptAt).ShouldBe((attempts + 1, Now.AddSeconds(seconds)));
    }

    // An order by amount has no quantity to fill: it is done when the exchange says it filled.
    [Test]
    public void Order_the_exchange_ended_as_filled_is_filled()
    {
        Order(LiveOrderStatus.Active).Completed().Status.ShouldBe(LiveOrderStatus.Filled);
    }

    [Test]
    public void Order_the_exchange_ended_as_cancelled_is_cancelled()
    {
        Order(LiveOrderStatus.CancelSent).Cancelled().Status.ShouldBe(LiveOrderStatus.Cancelled);
    }

    [Test]
    public void Refused_order_keeps_the_reason()
    {
        LiveOrder rejected = Order().Rejected("Insufficient balance.");

        (rejected.Status, rejected.RejectReason).ShouldBe((LiveOrderStatus.Rejected, "Insufficient balance."));
    }

    private static LiveOrder Order(LiveOrderStatus status = LiveOrderStatus.PendingSubmit)
    {
        return LiveOrder.New(Bot, 7, 2, "entry", OrderSide.Buy, OrderType.Limit, 100m, 0.5m, null, 50m, Now) with { Status = status };
    }
}