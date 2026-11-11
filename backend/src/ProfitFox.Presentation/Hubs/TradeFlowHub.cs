using Microsoft.AspNetCore.SignalR;

using ProfitFox.Application.MarketData;
using ProfitFox.Application.TradeFlow.Live;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Presentation.Hubs;

/// <summary>
///     The current bar of the trade flow of a pair (decision 10 of collect-trade-flow). A connection follows one pair of linear or spot at a time:
///     <c>Join</c> answers the state of the feed with its newest sums, then <c>TradeFlowMinute</c> and <c>TradeFlowStatus</c> messages come until
///     <c>Leave</c> or the disconnect. A pair of moex has no trade flow: <c>trade_flow_not_found</c>.
/// </summary>
public sealed class TradeFlowHub(TradeFlowFeeds feeds) : Hub
{
    public const string Path = "/api/hubs/trade-flow";

    private const string GroupKey = "group";

    [HubMethodName("Join")]
    public async Task<TradeFlowSnapshotMessage> JoinAsync(string category, string symbol)
    {
        if (!MarketDataCodes.TryParseCategory(category, out MarketCategory parsed) || parsed == MarketCategory.Moex)
        {
            throw new HubException("trade_flow_not_found");
        }

        var instrument = new Instrument(parsed, symbol);
        string group = TradeFlowBroadcast.Group(instrument);
        await LeaveGroupAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[GroupKey] = group;

        LiveTradeFlowSnapshot snapshot = await feeds.JoinAsync(Context.ConnectionId, instrument, Context.ConnectionAborted);

        // SignalR runs the invocations of a connection in the background: a disconnect may have overtaken this one, and its
        // OnDisconnectedAsync found nothing to leave. The token is cancelled before that call, so the viewer is taken off here.
        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            await feeds.LeaveAsync(Context.ConnectionId);
        }

        return TradeFlowSnapshotMessage.Of(snapshot);
    }

    [HubMethodName("Leave")]
    public async Task LeaveAsync()
    {
        await LeaveGroupAsync();
        await feeds.LeaveAsync(Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await feeds.LeaveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveGroupAsync()
    {
        if (Context.Items.Remove(GroupKey, out object? group))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, (string)group!);
        }
    }
}