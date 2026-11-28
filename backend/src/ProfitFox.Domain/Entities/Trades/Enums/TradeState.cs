namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     The state of a trade by an idea (change take-idea-trades, spec idea-trades «Состояния сделки»): the entry order is
///     written and not filled yet; the position is on the exchange; the close order is written and not filled yet; there is
///     no position, and the reason and the outcome are written; the entry did not happen; the position on the exchange does
///     not match the trade.
/// </summary>
public enum TradeState
{
    Pending,
    Open,
    Closing,
    Closed,
    Cancelled,
    Error
}
