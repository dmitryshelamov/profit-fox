using System.Globalization;

using ProfitFox.Application.TradeFlow.Live;

namespace ProfitFox.Presentation.Hubs;

/// <summary>
///     The sums of the current minute as the hub sends them: volumes and turnovers as decimal strings, times in UTC; <see cref="Since" /> is the
///     moment the feed subscribed, so a minute that began before it is incomplete.
/// </summary>
public sealed record TradeFlowMinuteMessage(
    DateTimeOffset OpenTime,
    string BuyVolume,
    string SellVolume,
    string BuyTurnover,
    string SellTurnover,
    int BuyTrades,
    int SellTrades,
    DateTimeOffset Since)
{
    public static TradeFlowMinuteMessage Of(LiveTradeFlow flow)
    {
        return new TradeFlowMinuteMessage(
            flow.Minute.OpenTime,
            Text(flow.Minute.BuyVolume),
            Text(flow.Minute.SellVolume),
            Text(flow.Minute.BuyTurnover),
            Text(flow.Minute.SellTurnover),
            flow.Minute.BuyTrades,
            flow.Minute.SellTrades,
            flow.Since);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}