using ProfitFox.Domain.Entities.Trades;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     A row of the list of trades: the trade and the outcome of its idea as a fraction, read when the list is given — null
///     while the idea is open or once it was removed.
/// </summary>
public sealed record TradeListItem(Trade Trade, double? IdeaResult);
