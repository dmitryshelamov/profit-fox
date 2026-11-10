using ProfitFox.Domain.Entities.Market.Enums;

namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>
///     The trades of a pair in one minute by the side of their taker: the volume in coins, the turnover in the quote coin (price × volume of
///     each trade) and the number of trades. A minute without trades has none.
/// </summary>
public sealed record TradeFlowMinute(
    DateTimeOffset OpenTime,
    decimal BuyVolume,
    decimal SellVolume,
    decimal BuyTurnover,
    decimal SellTurnover,
    int BuyTrades,
    int SellTrades)
{
    private static readonly long MinuteTicks = TimeSpan.FromMinutes(1).Ticks;

    /// <summary>The minute of the time of <paramref name="trade" /> with the trade in it.</summary>
    public static TradeFlowMinute Start(TradeEvent trade)
    {
        var openTime = new DateTimeOffset(trade.Time.UtcTicks - (trade.Time.UtcTicks % MinuteTicks), TimeSpan.Zero);

        return new TradeFlowMinute(openTime, 0m, 0m, 0m, 0m, 0, 0).Add(trade);
    }

    /// <summary>The minute with one more trade of it.</summary>
    public TradeFlowMinute Add(TradeEvent trade)
    {
        return trade.Side == TradeSide.Buy
            ? this with { BuyVolume = BuyVolume + trade.Quantity, BuyTurnover = BuyTurnover + (trade.Quantity * trade.Price), BuyTrades = BuyTrades + 1 }
            : this with { SellVolume = SellVolume + trade.Quantity, SellTurnover = SellTurnover + (trade.Quantity * trade.Price), SellTrades = SellTrades + 1 };
    }
}