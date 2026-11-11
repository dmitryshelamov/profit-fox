using System.Globalization;

using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Presentation.Contracts.TradeFlow;

/// <summary>The buys and sells of a bar by the side of the taker: the volume in coins, the turnover in USDT and the number of trades.</summary>
public sealed record TradeFlowBarResponse(
    DateTimeOffset OpenTime,
    string BuyVolume,
    string SellVolume,
    string BuyTurnover,
    string SellTurnover,
    int BuyTrades,
    int SellTrades)
{
    public static TradeFlowBarResponse Of(TradeFlowMinute bar)
    {
        return new TradeFlowBarResponse(
            bar.OpenTime,
            Text(bar.BuyVolume),
            Text(bar.SellVolume),
            Text(bar.BuyTurnover),
            Text(bar.SellTurnover),
            bar.BuyTrades,
            bar.SellTrades);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}