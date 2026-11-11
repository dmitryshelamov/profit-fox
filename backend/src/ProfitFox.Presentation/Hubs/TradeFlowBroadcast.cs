using Microsoft.AspNetCore.SignalR;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.TradeFlow.Live;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Presentation.Hubs;

/// <summary>Sends the messages of a pair to the group of its viewers on the hub of the trade flow.</summary>
internal sealed class TradeFlowBroadcast(IHubContext<TradeFlowHub> hub) : ITradeFlowBroadcast
{
    public static string Group(Instrument instrument)
    {
        return $"{instrument.Category.ToCode()}:{instrument.Symbol}";
    }

    public Task MinuteAsync(Instrument instrument, LiveTradeFlow flow)
    {
        return hub.Clients.Group(Group(instrument)).SendAsync("TradeFlowMinute", TradeFlowMinuteMessage.Of(flow));
    }

    public Task StatusAsync(Instrument instrument, FormingState state)
    {
        return hub.Clients.Group(Group(instrument)).SendAsync("TradeFlowStatus", FormingSnapshotMessage.StateName(state));
    }
}