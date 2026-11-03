using MediatR;

using Microsoft.AspNetCore.SignalR;

using ProfitFox.Application.Bots.Queries.GetBot;
using ProfitFox.Application.MarketData.FormingCandles;

namespace ProfitFox.Presentation.Hubs;

/// <summary>
///     The current candle of the chart of a bot. A connection follows one bot at a time: <c>Subscribe</c> answers the state of the
///     feed with its newest candle, then <c>FormingCandle</c> and <c>FormingStatus</c> messages come until <c>Unsubscribe</c> or the
///     disconnect. Status, position, orders and fills do not come here.
/// </summary>
public sealed class BotChartHub(ISender sender, FormingCandleFeeds feeds) : Hub
{
    public const string Path = "/api/hubs/bot-chart";

    private const string GroupKey = "group";

    public async Task<FormingSnapshotMessage> Subscribe(Guid botId)
    {
        GetBotResult bot = await sender.Send(new GetBotQuery(botId), Context.ConnectionAborted) ?? throw new HubException("bot_not_found");
        var series = new FormingSeries(bot.Bot.Instrument, bot.Bot.Interval);
        string group = BotChartBroadcast.Group(series);

        await LeaveGroupAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[GroupKey] = group;

        FormingSnapshot snapshot = await feeds.JoinAsync(Context.ConnectionId, series, Context.ConnectionAborted);

        // SignalR runs the invocations of a connection in the background: a disconnect may have overtaken this one, and its
        // OnDisconnectedAsync found nothing to leave. The token is cancelled before that call, so the viewer is taken off here.
        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            await feeds.LeaveAsync(Context.ConnectionId);
        }

        return FormingSnapshotMessage.Of(snapshot);
    }

    public async Task Unsubscribe()
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