using ProfitFox.Application.Ports.LiveTrading;

using Tinkoff.InvestApi.V1;

using ExchangeOrderState = ProfitFox.Application.Ports.LiveTrading.ExchangeOrderState;
using Instrument = ProfitFox.Domain.ValueObjects.MarketData.Instrument;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     An order of T-Invest as the system knows it (decision 7), from the unary <c>OrderState</c>: the volume in shares (the lots executed
///     × the lot), the value the sum of its trades, the fee the commission executed so far, in roubles; its trades are its stages, without
///     a fee of their own. No text of the broker reaches a reason.
/// </summary>
internal static class TInvestOrderStates
{
    public const string Roubles = "RUB";

    public static ExchangeOrder Order(OrderState state, string clientId, int lot)
    {
        ExchangeOrderState status = state.ExecutionReportStatus switch
        {
            OrderExecutionReportStatus.ExecutionReportStatusFill => ExchangeOrderState.Filled,
            OrderExecutionReportStatus.ExecutionReportStatusCancelled => ExchangeOrderState.Cancelled,
            OrderExecutionReportStatus.ExecutionReportStatusRejected => ExchangeOrderState.Rejected,
            _ => ExchangeOrderState.Open
        };

        return new ExchangeOrder(
            clientId,
            state.OrderId,
            status,
            state.LotsExecuted * lot,
            status == ExchangeOrderState.Rejected ? "T-Invest rejected the order." : null,
            state.Stages.Sum(stage => stage.Price.ToDecimal() * stage.Quantity * lot),
            state.ExecutedCommission?.ToDecimal() ?? 0m,
            Roubles);
    }

    public static IReadOnlyList<ExchangeFill> Fills(OrderState state, string clientId, int lot, Instrument instrument)
    {
        return
        [
            .. state.Stages.Select(stage => new ExchangeFill(
                stage.TradeId,
                clientId,
                stage.Quantity * lot,
                stage.Price.ToDecimal(),
                0m,
                Roubles,
                stage.ExecutionTime.ToUtcMoment(),
                instrument,
                state.OrderId))
        ];
    }

    /// <summary>Whether the trades add up to the lots executed: two parts of one answer may disagree, and then the state is read again.</summary>
    public static bool Complete(OrderState state)
    {
        return state.Stages.Sum(stage => stage.Quantity) == state.LotsExecuted;
    }
}