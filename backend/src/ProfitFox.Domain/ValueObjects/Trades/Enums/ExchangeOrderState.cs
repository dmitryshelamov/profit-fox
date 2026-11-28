namespace ProfitFox.Domain.ValueObjects.Trades.Enums;

/// <summary>
///     Where an order is at the exchange (<c>orderStatus</c> of Bybit): taken and waiting, partly filled, filled, refused,
///     or cancelled — a market order the book could not fill whole is cancelled with what was filled.
/// </summary>
public enum ExchangeOrderState
{
    New,
    PartiallyFilled,
    Filled,
    Rejected,
    Cancelled
}
