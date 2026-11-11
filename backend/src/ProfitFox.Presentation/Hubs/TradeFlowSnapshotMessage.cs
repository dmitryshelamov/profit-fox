using ProfitFox.Application.TradeFlow.Live;

namespace ProfitFox.Presentation.Hubs;

/// <summary>What <c>Join</c> answers: the state of the feed (<c>connecting</c>, <c>live</c> or <c>noData</c>) and its newest sums, <c>null</c> before the first trade.</summary>
public sealed record TradeFlowSnapshotMessage(string State, TradeFlowMinuteMessage? Flow)
{
    public static TradeFlowSnapshotMessage Of(LiveTradeFlowSnapshot snapshot)
    {
        return new TradeFlowSnapshotMessage(FormingSnapshotMessage.StateName(snapshot.State), snapshot.Flow is { } flow ? TradeFlowMinuteMessage.Of(flow) : null);
    }
}