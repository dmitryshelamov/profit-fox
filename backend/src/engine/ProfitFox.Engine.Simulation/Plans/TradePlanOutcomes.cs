using ProfitFox.Engine.Simulation.Market;
using ProfitFox.Engine.Simulation.Orders;

namespace ProfitFox.Engine.Simulation.Plans;

/// <summary>
///     Outcomes of trades of a plan on the minutes of one pair: each enters with a market order for an amount of 1 at its moment and exits
///     with a market order for the whole quantity at the end of the horizon, on <see cref="ExchangeSimulator" />.
/// </summary>
public static class TradePlanOutcomes
{
    /// <summary>
    ///     The outcome of each entry moment, in their order; an outcome depends on no other entry.
    /// </summary>
    /// <exception cref="ArgumentException">An entry moment is not a whole minute.</exception>
    public static IReadOnlyList<TradeOutcome> Compute(MinutePath path, TradePlan plan, Costs costs, IReadOnlyList<DateTimeOffset> entryMoments)
    {
        var simulator = new ExchangeSimulator(path, costs);
        return entryMoments.Select(moment => Compute(path, simulator, plan, moment)).ToList();
    }

    private static TradeOutcome Compute(MinutePath path, ExchangeSimulator simulator, TradePlan plan, DateTimeOffset moment)
    {
        bool isLong = plan.Side == PositionSide.Long;
        DateTimeOffset end = moment + plan.Horizon;
        Fill? entry = simulator.Execute(MarketOrder.ForAmount(moment, isLong ? OrderSide.Buy : OrderSide.Sell, amount: 1m));
        if (!path.IsCovered(moment, end))
        {
            return new TradeOutcome(moment, TradeStatus.ExcludedCoverage);
        }

        if (entry is null || entry.Time >= end)
        {
            return new TradeOutcome(moment, TradeStatus.ExcludedNoTrade);
        }

        Fill? exit = simulator.Execute(MarketOrder.ForQuantity(end, isLong ? OrderSide.Sell : OrderSide.Buy, entry.Quantity));
        if (exit is null)
        {
            return new TradeOutcome(moment, TradeStatus.ExcludedCoverage);
        }

        // Judged from the entry moment, not from the entry fill, as the Python bench does.
        decimal? funding = simulator.Funding(plan.Side, entry.Quantity, entry.Time, exit.Time);
        if (moment < path.FundingCoverage.Start || funding is null)
        {
            return new TradeOutcome(moment, TradeStatus.ExcludedFunding);
        }

        decimal sign = isLong ? 1 : -1;
        decimal gross = sign * (exit.MarketPrice / entry.MarketPrice - 1);
        decimal traded = sign * (exit.Price / entry.Price - 1);
        decimal fees = entry.Fee + exit.Fee;
        return new TradeOutcome(
            moment,
            TradeStatus.Ok,
            entry.Time,
            entry.Price,
            exit.Time,
            exit.Price,
            gross,
            gross - traded,
            fees,
            funding,
            traded - fees + funding);
    }
}