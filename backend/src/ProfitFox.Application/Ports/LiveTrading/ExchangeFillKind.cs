namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>What made an execution: an order that traded, or the exchange closing a position by itself.</summary>
public enum ExchangeFillKind
{
    Trade,

    /// <summary>The exchange took the position over at its bankruptcy price (Bybit <c>BustTrade</c>).</summary>
    Liquidation,

    /// <summary>The exchange closed the position against a liquidated one by auto-deleveraging (Bybit <c>AdlTrade</c>).</summary>
    Adl
}