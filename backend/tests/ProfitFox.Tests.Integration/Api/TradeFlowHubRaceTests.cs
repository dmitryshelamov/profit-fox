using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.TradeFlow.Live;
using ProfitFox.Presentation.Hubs;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>SignalR runs the invocations of a connection in the background, so a disconnect can overtake <c>Join</c>.</summary>
public class TradeFlowHubRaceTests
{
    [Test]
    public async Task Join_of_a_connection_that_has_already_gone_leaves_the_feed_again()
    {
        IAsyncDisposable subscription = Substitute.For<IAsyncDisposable>();
        ITradeFlowStreams streams = Substitute.For<ITradeFlowStreams>();
        streams.SubscribeTradesAsync(default!, default!, default!, default!, default!, default).ReturnsForAnyArgs(Task.FromResult(subscription));
        var feeds = new TradeFlowFeeds(streams, Substitute.For<ITradeFlowBroadcast>(), new FakeTimeProvider());
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("gone");
        context.ConnectionAborted.Returns(aborted.Token);
        context.Items.Returns(new Dictionary<object, object?>());
        var hub = new TradeFlowHub(feeds) { Context = context, Groups = Substitute.For<IGroupManager>() };

        await hub.JoinAsync("linear", "SOLUSDT");

        await subscription.Received(1).DisposeAsync();
    }
}