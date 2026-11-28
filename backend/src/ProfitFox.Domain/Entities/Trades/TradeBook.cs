namespace ProfitFox.Domain.Entities.Trades;

/// <summary>
///     A trade with its orders and fills, as one change of the store takes it (change take-idea-trades, design.md, decision
///     12): read under the lock of the trade, changed, and written back whole in the same transaction. <see cref="Known" />
///     are the ids of executions asked about that the store already keeps for any trade.
/// </summary>
public sealed class TradeBook(Trade trade, List<TradeOrder> orders, List<TradeFill> fills, IReadOnlySet<string> known)
{
    public Trade Trade { get; } = trade;

    public List<TradeOrder> Orders { get; } = orders;

    public List<TradeFill> Fills { get; } = fills;

    public IReadOnlySet<string> Known { get; } = known;

    /// <summary>
    ///     The next number of an order of the trade.
    /// </summary>
    public int NextNumber => Orders.Count == 0 ? 1 : Orders.Max(order => order.Number) + 1;

    /// <summary>
    ///     Puts the order in place of the one of its number, or adds it.
    /// </summary>
    public void Put(TradeOrder order)
    {
        int index = Orders.FindIndex(other => other.Number == order.Number);
        if (index < 0)
        {
            Orders.Add(order);
        }
        else
        {
            Orders[index] = order;
        }
    }
}
