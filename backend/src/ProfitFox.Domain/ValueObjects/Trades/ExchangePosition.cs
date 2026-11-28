using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     The position of a pair on the account as the exchange tells it: its side — null for an empty position —, its size, the
///     mean price, the stop and the target of the position — null when none —, the leverage, whether it is of the one-way mode
///     (<c>positionIdx</c> 0) or of the hedge mode, its result now and the mark price, when the exchange sends them.
/// </summary>
public sealed record ExchangePosition(
    string Symbol,
    TradeSide? Side,
    decimal Size,
    decimal? AveragePrice,
    decimal? Stop,
    decimal? Target,
    decimal? Leverage,
    bool OneWay,
    decimal? Unrealised,
    decimal? MarkPrice) : ExchangeMessage(Symbol)
{
    public static ExchangePosition Empty(string symbol)
    {
        return new ExchangePosition(symbol, null, 0m, null, null, null, null, true, null, null);
    }
}
