namespace ProfitFox.Domain.ValueObjects.Trades.Enums;

/// <summary>
///     The kind of a fill as the exchange marks it (<c>execType</c> of Bybit; design.md, decision 5): a trade, a liquidation
///     (<c>BustTrade</c>), an auto-deleveraging (<c>AdlTrade</c>), a payment of funding, or another kind.
/// </summary>
public enum ExchangeFillType
{
    Trade,
    Liquidation,
    Adl,
    Funding,
    Other
}
