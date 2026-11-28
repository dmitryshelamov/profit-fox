namespace ProfitFox.Domain.ValueObjects.Trades.Enums;

/// <summary>
///     Which conditional order of the position a fill came from (<c>stopOrderType</c> of Bybit; design.md, decision 5): none,
///     the stop of the position, its target, or another one.
/// </summary>
public enum ExchangeStopType
{
    None,
    StopLoss,
    TakeProfit,
    Other
}
