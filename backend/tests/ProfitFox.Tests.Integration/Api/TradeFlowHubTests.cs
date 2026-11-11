using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>The hub of the current bar of the trade flow, <c>/api/hubs/trade-flow</c>, on the streams of a fake Bybit (task 7.3 of collect-trade-flow).</summary>
public class TradeFlowHubTests
{
    private const string Topic = "publicTrade.SOLUSDT";

    private FakeBybitSocketServer _server = null!;
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<HubConnection> _hubs = [];

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0, BybitSocketAddress = _server.Address };
        _client = _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (HubConnection hub in _hubs)
        {
            await hub.DisposeAsync();
        }

        _client.Dispose();
        await _factory.DisposeAsync();
        await _server.DisposeAsync();
    }

    // Scenario "Сделки идут": the snapshot, then the sums of the current minute and the state.
    [Test]
    public async Task Join_gives_the_snapshot_and_then_the_sums_of_the_current_minute()
    {
        (HubConnection hub, Messages messages) = await ConnectAsync();
        DateTimeOffset at = _factory.Time.GetUtcNow();

        JsonElement snapshot = await hub.InvokeAsync<JsonElement>("Join", "linear", "SOLUSDT");
        await _server.WaitForTopicAsync(Topic);
        await _server.PushAsync(Topic, [FakeBybitSocketServer.Trade("SOLUSDT", "Buy", 2m, 150m, at), FakeBybitSocketServer.Trade("SOLUSDT", "Sell", 1m, 149m, at)]);

        snapshot.GetProperty("state").GetString().ShouldBe("connecting");
        snapshot.GetProperty("flow").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonElement flow = await messages.NextAsync("TradeFlowMinute");
        (await messages.NextAsync("TradeFlowStatus")).GetString().ShouldBe("live");
        flow.GetProperty("openTime").GetDateTimeOffset().ShouldBe(at.AddTicks(-(at.UtcTicks % TimeSpan.TicksPerMinute)));
        flow.GetProperty("since").GetDateTimeOffset().ShouldBe(at);
        decimal.Parse(flow.GetProperty("buyTurnover").GetString()!, CultureInfo.InvariantCulture).ShouldBeGreaterThan(0m);
    }

    // Scenarios "Две вкладки" and "Последняя вкладка закрыта".
    [Test]
    public async Task Two_viewers_of_a_pair_share_one_subscription_and_the_last_to_leave_takes_it_off()
    {
        (HubConnection first, _) = await ConnectAsync();
        (HubConnection second, _) = await ConnectAsync();
        await first.InvokeAsync<JsonElement>("Join", "linear", "SOLUSDT");
        await second.InvokeAsync<JsonElement>("Join", "linear", "SOLUSDT");
        await _server.WaitForTopicAsync(Topic);

        _server.Subscribes(Topic).ShouldBe(1);
        await first.StopAsync();
        await Task.Delay(300);
        _server.Subscribed(Topic).ShouldBeTrue();
        await second.InvokeAsync("Leave");
        await WaitAsync(() => !_server.Subscribed(Topic));
    }

    // Decision 10: the hub does not open for moex.
    [TestCase("moex", "SBER")]
    [TestCase("inverse", "SOLUSDT")]
    public async Task Pair_without_a_trade_flow_is_refused(string category, string symbol)
    {
        (HubConnection hub, _) = await ConnectAsync();

        HubException error = await Should.ThrowAsync<HubException>(() => hub.InvokeAsync<JsonElement>("Join", category, symbol));

        error.Message.ShouldContain("trade_flow_not_found");
    }

    private async Task<(HubConnection Hub, Messages Messages)> ConnectAsync()
    {
        HubConnection hub = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "api/hubs/trade-flow"),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.WebSocketFactory = (context, cancellationToken) => new ValueTask<System.Net.WebSockets.WebSocket>(
                        _factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken));
                })
            .Build();
        var messages = new Messages();
        hub.On<JsonElement>("TradeFlowMinute", flow => messages.Add("TradeFlowMinute", flow));
        hub.On<JsonElement>("TradeFlowStatus", status => messages.Add("TradeFlowStatus", status));
        await hub.StartAsync();
        _hubs.Add(hub);

        return (hub, messages);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class Messages
    {
        private readonly ConcurrentDictionary<string, System.Collections.Concurrent.BlockingCollection<JsonElement>> _queues = new ConcurrentDictionary<string, System.Collections.Concurrent.BlockingCollection<JsonElement>>();

        public void Add(string name, JsonElement message)
        {
            _queues.GetOrAdd(name, _ => []).Add(message.Clone());
        }

        public async Task<JsonElement> NextAsync(string name)
        {
            System.Collections.Concurrent.BlockingCollection<JsonElement> queue = _queues.GetOrAdd(name, _ => []);

            return await Task.Run(() => queue.TryTake(out JsonElement message, TimeSpan.FromSeconds(10)) ? message : throw new TimeoutException($"No {name} message came."));
        }
    }
}