using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>The bot chart hub, <c>/api/hubs/bot-chart</c>, on the streams of a fake Bybit.</summary>
public class BotChartHubTests
{
    private const string Topic = "kline.60.BTCUSDT";
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private FakeBybitSocketServer _server = null!;
    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly List<HubConnection> _hubs = [];

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0, BybitSocketAddress = _server.Address };
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

    // Scenarios "Цена меняется" and "Состояние канала": the snapshot, then updates and the state.
    [Test]
    public async Task Subscribe_gives_the_snapshot_and_then_the_updates_of_the_current_candle()
    {
        Guid bot = await CreateBotAsync("BTCUSDT", "60");
        (HubConnection hub, Messages messages) = await ConnectAsync();

        JsonElement snapshot = await hub.InvokeAsync<JsonElement>("Subscribe", bot);
        await _server.WaitForTopicAsync(Topic);
        await _server.PushAsync(Topic, [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 101.5m, confirm: false, volume: 2.25m, at: Open.AddMinutes(10))]);

        snapshot.GetProperty("state").GetString().ShouldBe("connecting");
        snapshot.GetProperty("candle").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonElement candle = await messages.NextAsync("FormingCandle");
        (candle.GetProperty("openTime").GetDateTimeOffset(), candle.GetProperty("close").GetString(), candle.GetProperty("volume").GetString(), candle.GetProperty("isClosed").GetBoolean())
            .ShouldBe((Open, "101.5", "2.25", false));
        (await messages.NextAsync("FormingStatus")).GetString().ShouldBe("live");
    }

    [Test]
    public async Task A_later_viewer_gets_the_last_candle_in_the_snapshot()
    {
        Guid bot = await CreateBotAsync("BTCUSDT", "60");
        (HubConnection first, Messages firstMessages) = await ConnectAsync();
        await first.InvokeAsync<JsonElement>("Subscribe", bot);
        await _server.WaitForTopicAsync(Topic);
        await _server.PushAsync(Topic, [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 101.5m, confirm: false, at: Open.AddMinutes(10))]);
        await firstMessages.NextAsync("FormingCandle");
        (HubConnection second, _) = await ConnectAsync();

        JsonElement snapshot = await second.InvokeAsync<JsonElement>("Subscribe", bot);

        snapshot.GetProperty("state").GetString().ShouldBe("live");
        snapshot.GetProperty("candle").GetProperty("close").GetString().ShouldBe("101.5");
    }

    [Test]
    public async Task Unknown_bot_is_the_error_bot_not_found()
    {
        (HubConnection hub, _) = await ConnectAsync();

        HubException error = await Should.ThrowAsync<HubException>(() => hub.InvokeAsync<JsonElement>("Subscribe", Guid.NewGuid()));

        error.Message.ShouldContain("bot_not_found");
    }

    // Scenarios "Две вкладки" and "Последняя вкладка закрыта".
    [Test]
    public async Task Two_bots_of_one_series_share_one_subscription_and_closing_both_takes_it_off()
    {
        Guid one = await CreateBotAsync("BTCUSDT", "60");
        Guid two = await CreateBotAsync("BTCUSDT", "60");
        (HubConnection first, _) = await ConnectAsync();
        (HubConnection second, _) = await ConnectAsync();
        await first.InvokeAsync<JsonElement>("Subscribe", one);
        await second.InvokeAsync<JsonElement>("Subscribe", two);
        await _server.WaitForTopicAsync(Topic);

        _server.Subscribes(Topic).ShouldBe(1);
        await first.StopAsync();
        await Task.Delay(300);
        _server.Subscribed(Topic).ShouldBeTrue();
        await second.InvokeAsync("Unsubscribe");
        await WaitAsync(() => !_server.Subscribed(Topic));
    }

    [Test]
    public async Task Subscribing_to_another_bot_leaves_the_previous_series()
    {
        Guid btc = await CreateBotAsync("BTCUSDT", "60");
        Guid eth = await CreateBotAsync("ETHUSDT", "15");
        (HubConnection hub, _) = await ConnectAsync();
        await hub.InvokeAsync<JsonElement>("Subscribe", btc);
        await _server.WaitForTopicAsync(Topic);

        await hub.InvokeAsync<JsonElement>("Subscribe", eth);

        await _server.WaitForTopicAsync("kline.15.ETHUSDT");
        await WaitAsync(() => !_server.Subscribed(Topic));
    }

    // Scenarios "Связь с Bybit оборвалась" and "Связь восстановилась".
    [Test]
    public async Task A_drop_means_no_data_and_the_next_update_after_the_restore_means_live()
    {
        Guid bot = await CreateBotAsync("BTCUSDT", "60");
        (HubConnection hub, Messages messages) = await ConnectAsync();
        await hub.InvokeAsync<JsonElement>("Subscribe", bot);
        await _server.WaitForTopicAsync(Topic);
        await _server.PushAsync(Topic, [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 101m, confirm: false, at: Open.AddMinutes(10))]);
        (await messages.NextAsync("FormingStatus")).GetString().ShouldBe("live");

        await _server.DropAsync();

        (await messages.NextAsync("FormingStatus")).GetString().ShouldBe("noData");
        await WaitAsync(() => _server.Subscribed(Topic));
        await _server.PushAsync(Topic, [FakeBybitSocketServer.Kline(Open, TimeSpan.FromHours(1), 102m, confirm: false, at: Open.AddMinutes(11))]);
        (await messages.NextAsync("FormingStatus")).GetString().ShouldBe("live");
    }

    private async Task<(HubConnection Hub, Messages Messages)> ConnectAsync()
    {
        HubConnection hub = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "api/hubs/bot-chart"),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.WebSocketFactory = (context, cancellationToken) => new ValueTask<System.Net.WebSockets.WebSocket>(
                        _factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken));
                })
            .Build();
        var messages = new Messages();
        hub.On<JsonElement>("FormingCandle", candle => messages.Add("FormingCandle", candle));
        hub.On<JsonElement>("FormingStatus", status => messages.Add("FormingStatus", status));
        await hub.StartAsync();
        _hubs.Add(hub);

        return (hub, messages);
    }

    private async Task<Guid> CreateBotAsync(string symbol, string interval)
    {
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = $"EMA {Guid.NewGuid()}", definition = StrategyFixtures.Element("ema") });
        Guid ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _factory.Bybit.Instruments.Add(("spot", symbol));
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/bots", new { name = $"{symbol} {Guid.NewGuid()}", strategyId = ema, category = "spot", symbol, interval, balance = 1000 });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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