using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Bots.Queries.GetBot;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Presentation.Hubs;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>SignalR runs the invocations of a connection in the background, so a disconnect can overtake <c>Subscribe</c>.</summary>
public class BotChartHubRaceTests
{
    [Test]
    public async Task Subscribe_of_a_connection_that_has_already_gone_leaves_the_feed_again()
    {
        var bot = new Bot(Guid.NewGuid(), "EMA", Guid.NewGuid(), new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, 1000m, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBotQuery>(), Arg.Any<CancellationToken>()).Returns(new GetBotResult(bot, null, default(LiveBot)!));
        IAsyncDisposable subscription = Substitute.For<IAsyncDisposable>();
        IFormingCandleStreams streams = Substitute.For<IFormingCandleStreams>();
        streams.SubscribeAsync(default!, default, default!, default!, default!, default!, default).ReturnsForAnyArgs(Task.FromResult(subscription));
        var feeds = new FormingCandleFeeds(streams, Substitute.For<IFormingCandleBroadcast>(), new FakeTimeProvider());
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("gone");
        context.ConnectionAborted.Returns(aborted.Token);
        context.Items.Returns(new Dictionary<object, object?>());
        var hub = new BotChartHub(sender, feeds) { Context = context, Groups = Substitute.For<IGroupManager>() };

        await hub.Subscribe(bot.Id);

        await subscription.Received(1).DisposeAsync();
    }
}