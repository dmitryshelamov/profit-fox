using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     What a backtest keeps of an order with a trade to its end (change fix-backtest-random-control, design.md, decision 5):
///     its week, its moment, its side and its result after costs. Its difference is made at the end from the cell of control
///     of its moment and side.
/// </summary>
public readonly record struct OrderTrade(long Week, DateTimeOffset Moment, EntrySide Side, double Y)
{
    /// <summary>
    ///     The moment and the side, the key of the cell of control of the order.
    /// </summary>
    public PairOrder Order => new PairOrder(Moment, Side);

    /// <summary>
    ///     The order of a row with a trade; null for a row whose trade is left out.
    /// </summary>
    public static OrderTrade? Of(EventRow row)
    {
        return row is { Status: TradeStatus.Ok, Event: { } fields } ? new OrderTrade(row.Block, row.Moment, row.Side, fields.Y) : null;
    }
}