using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Strategy.Execution;

namespace ProfitFox.Engine.Simulation.Orders;

/// <summary>
///     Fills orders on the minutes of one pair and computes the funding of positions (docs/adr/0012-plan-outcome-on-the-order-simulator.md).
///     A market order placed at T fills whole at the open of the first minute with trades starting at or after T, worse by the slippage;
///     nothing before T matters.
/// </summary>
public sealed class ExchangeSimulator
{
    private readonly MinutePath _path;
    private readonly Costs _costs;

    public ExchangeSimulator(MinutePath path, Costs costs)
    {
        _path = path;
        _costs = costs;
    }

    /// <summary>
    ///     The fill of a market order; null when a minute between the order and the first minute with trades is not covered, or the path
    ///     ends first.
    /// </summary>
    /// <exception cref="ArgumentException">The order is not placed at a whole minute.</exception>
    public Fill? Execute(MarketOrder order)
    {
        if (!MinutePath.IsWholeMinute(order.PlacedAt))
        {
            throw new ArgumentException($"The order at {order.PlacedAt:O} is not placed at a whole minute.", nameof(order));
        }

        MinuteCandle? minute = _path.FirstTradeAtOrAfter(order.PlacedAt);
        if (minute is null)
        {
            return null;
        }

        decimal price = order.Side == OrderSide.Buy ? minute.Open * (1 + _costs.Slippage) : minute.Open * (1 - _costs.Slippage);
        decimal quantity = order.Quantity ?? order.Amount!.Value / price;
        return new Fill(minute.OpenTime, price, minute.Open, quantity, quantity * price * _costs.TakerFee);
    }

    /// <summary>
    ///     The funding a position of <paramref name="quantity" /> received, in the quote asset, for being open from the fill at
    ///     <paramref name="from" /> to the fill at <paramref name="to" />: every payment at <c>from &lt; t ≤ to</c> at the close of the last
    ///     minute with trades before it; below zero when the position paid. Null when the payments are not known for <c>[from, to]</c>.
    /// </summary>
    public decimal? Funding(PositionSide side, decimal quantity, DateTimeOffset from, DateTimeOffset to)
    {
        if (from < _path.FundingCoverage.Start || to >= _path.FundingCoverage.End)
        {
            return null;
        }

        decimal paidByLong = 0;
        foreach (FundingPayment payment in _path.PaymentsAfterUpTo(from, to))
        {
            paidByLong += payment.Rate * quantity * (_path.LastTradedCloseAt(payment.Time) ?? 0);
        }

        return side == PositionSide.Long ? -paidByLong : paidByLong;
    }
}