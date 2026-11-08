using Google.Protobuf.WellKnownTypes;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: an order of T-Invest as the system knows it, its volume in shares (lots × lot) and its value the sum of its trades.</summary>
public class TInvestOrderStatesTests
{
    private const int Lot = 10;
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2024-03-11T07:15:00Z");

    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusNew, ExchangeOrderState.Open)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill, ExchangeOrderState.Open)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusFill, ExchangeOrderState.Filled)]
    [TestCase(OrderExecutionReportStatus.ExecutionReportStatusCancelled, ExchangeOrderState.Cancelled)]
    public void Status_of_the_order_is_its_state(OrderExecutionReportStatus status, ExchangeOrderState state)
    {
        TInvestOrderStates.Order(State(status), "b-1", Lot).State.ShouldBe(state);
    }

    // The text of the broker never reaches the reason.
    [Test]
    public void Rejected_order_is_rejected_by_the_broker()
    {
        ExchangeOrder order = TInvestOrderStates.Order(State(OrderExecutionReportStatus.ExecutionReportStatusRejected), "b-1", Lot);

        (order.State, order.RejectReason).ShouldBe((ExchangeOrderState.Rejected, "T-Invest rejected the order."));
    }

    // 2 lots of 10 shares at 250.5 and 1 lot at 250.6 with 3.76 ₽ of commission so far.
    [Test]
    public void Totals_of_the_order_are_in_shares_and_roubles()
    {
        OrderState state = State(OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill, executed: 3, commission: 3.76m, Stage("t-1", 250.5m, 2), Stage("t-2", 250.6m, 1));

        ExchangeOrder order = TInvestOrderStates.Order(state, "b-1", Lot);

        order.ShouldBe(new ExchangeOrder("b-1", "R-1", ExchangeOrderState.Open, 30m, null, 7516m, 3.76m, "RUB"));
    }

    [Test]
    public void Order_without_a_commission_yet_has_no_fee()
    {
        TInvestOrderStates.Order(new OrderState { OrderId = "R-1", ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusNew }, "b-1", Lot).Fee.ShouldBe(0m);
    }

    // The fee comes with the order, so the trades carry none.
    [Test]
    public void Trades_of_the_order_are_its_stages_in_shares()
    {
        OrderState state = State(OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 3, commission: 3.76m, Stage("t-1", 250.5m, 2), Stage("t-2", 250.6m, 1));
        var sber = new Instrument(MarketCategory.Moex, "SBER");

        IReadOnlyList<ExchangeFill> fills = TInvestOrderStates.Fills(state, "b-1", Lot, sber);

        fills.ShouldBe(
        [
            new ExchangeFill("t-1", "b-1", 20m, 250.5m, 0m, "RUB", At, sber, "R-1"),
            new ExchangeFill("t-2", "b-1", 10m, 250.6m, 0m, "RUB", At.AddSeconds(1), sber, "R-1")
        ]);
    }

    // Two parts of one answer that disagree (ProfitGraph): the state is read again.
    [TestCase(3, true)]
    [TestCase(2, false)]
    public void Trades_that_do_not_add_up_to_the_executed_lots_are_not_complete(int executed, bool complete)
    {
        OrderState state = State(OrderExecutionReportStatus.ExecutionReportStatusFill, executed: executed, commission: 0m, Stage("t-1", 250.5m, 3));

        TInvestOrderStates.Complete(State(OrderExecutionReportStatus.ExecutionReportStatusFill, executed: 3, commission: 0m, Stage("t-1", 250.5m, 2))).ShouldBeFalse();
        TInvestOrderStates.Complete(state).ShouldBe(complete);
    }

    private static OrderState State(OrderExecutionReportStatus status, long executed = 0, decimal commission = 0m, params OrderStage[] stages)
    {
        return new OrderState
        {
            OrderId = "R-1",
            OrderRequestId = Guid.NewGuid().ToString(),
            ExecutionReportStatus = status,
            LotsRequested = 5,
            LotsExecuted = executed,
            ExecutedCommission = Money(commission),
            Stages = { stages }
        };
    }

    private static OrderStage Stage(string id, decimal price, long lots)
    {
        int second = id == "t-1" ? 0 : 1;

        return new OrderStage { TradeId = id, Price = Money(price), Quantity = lots, ExecutionTime = Timestamp.FromDateTimeOffset(At.AddSeconds(second)) };
    }

    private static MoneyValue Money(decimal value)
    {
        var quotation = value.ToQuotation();

        return new MoneyValue { Currency = "rub", Units = quotation.Units, Nano = quotation.Nano };
    }
}