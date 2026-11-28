namespace ProfitFox.Domain.Entities.Trades.Enums;

/// <summary>
///     Why a trade closed (spec idea-trades «Причина закрытия»): by its target or stop on the exchange, at the end of its
///     idea, by the owner's button, by the emergency stop, because its stop could not be put back, by a liquidation, by an
///     auto-deleveraging of the exchange, or by a fill on Bybit the bench did not make.
/// </summary>
public enum TradeCloseReason
{
    Target,
    Stop,
    Time,
    Manual,
    Halt,
    NoStop,
    Liquidation,
    Adl,
    Exchange
}
