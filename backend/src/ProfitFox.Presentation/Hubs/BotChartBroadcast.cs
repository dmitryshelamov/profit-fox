using Microsoft.AspNetCore.SignalR;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Presentation.Hubs;

/// <summary>Sends the messages of a series to the group of its viewers on the bot chart hub.</summary>
internal sealed class BotChartBroadcast(IHubContext<BotChartHub> hub) : IFormingCandleBroadcast
{
    public static string Group(FormingSeries series)
    {
        return $"{series.Instrument.Category.ToCode()}:{series.Instrument.Symbol}:{series.Interval.ToCode()}";
    }

    public Task CandleAsync(FormingSeries series, FormingCandle candle)
    {
        return hub.Clients.Group(Group(series)).SendAsync("FormingCandle", FormingCandleMessage.Of(candle));
    }

    public Task StatusAsync(FormingSeries series, FormingState state)
    {
        return hub.Clients.Group(Group(series)).SendAsync("FormingStatus", FormingSnapshotMessage.StateName(state));
    }
}