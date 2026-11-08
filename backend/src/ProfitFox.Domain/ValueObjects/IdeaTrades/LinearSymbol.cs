namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>
///     The symbol of <c>linear</c> on the account: its leverage, the position (a short below zero) and the number of open orders as the exchange has
///     them, and the leverages of the trades by ideas of the account on it that are open or wait for their entry, none for a trade whose leverage is
///     unknown. A trade that waits may have sent nothing to the exchange yet.
/// </summary>
public sealed record LinearSymbol(decimal Leverage, decimal Position, int OpenOrders, IReadOnlyCollection<int?> Trades)
{
    /// <summary>Whether something of the account stands on the symbol: a position, an open order or a trade by an idea.</summary>
    public bool IsHeld => Position != 0m || OpenOrders > 0 || Trades.Count > 0;
}