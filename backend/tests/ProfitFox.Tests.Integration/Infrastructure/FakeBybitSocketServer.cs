using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     The streams of Bybit v5 on a local port: <c>/v5/public/spot</c>, <c>/v5/public/linear</c> and <c>/v5/private</c>. It
///     answers <c>auth</c>, <c>subscribe</c>, <c>unsubscribe</c> and <c>ping</c> as Bybit does, pushes the messages a test gives
///     it and counts the connections and subscriptions clients make.
/// </summary>
public sealed class FakeBybitSocketServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<WebSocket, Connection> _connections = new ConcurrentDictionary<WebSocket, Connection>();

    private FakeBybitSocketServer(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Like <c>ws://127.0.0.1:5297</c>.</summary>
    public string Address { get; private set; } = "";

    /// <summary>Topics a subscription to is refused, as Bybit refuses one it does not serve.</summary>
    public ConcurrentDictionary<string, bool> RefusedTopics { get; } = new ConcurrentDictionary<string, bool>();

    /// <summary>How many times a client authenticated on the private stream.</summary>
    public int Authentications => _authentications;

    private readonly ConcurrentDictionary<string, int> _subscribes = new ConcurrentDictionary<string, int>();
    private int _authentications;

    /// <summary>The connections open now to <paramref name="path" />, such as <c>/v5/public/linear</c> or <c>/v5/private</c>.</summary>
    public int Connections(string path)
    {
        return _connections.Values.Count(connection => connection.Path == path);
    }

    /// <summary>How many times a client asked to subscribe to <paramref name="topic" />.</summary>
    public int Subscribes(string topic)
    {
        return _subscribes.GetValueOrDefault(topic);
    }

    /// <summary>Whether a client is subscribed to <paramref name="topic" /> now.</summary>
    public bool Subscribed(string topic)
    {
        return _connections.Values.Any(connection => connection.Topics.ContainsKey(topic));
    }

    public static async Task<FakeBybitSocketServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        var server = new FakeBybitSocketServer(app);
        app.UseWebSockets();
        app.Map("/v5/{*path}", server.AcceptAsync);
        await app.StartAsync();
        string http = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Address = http.Replace("http://", "ws://", StringComparison.Ordinal);

        return server;
    }

    /// <summary>Waits until a client is subscribed to <paramref name="topic" />, such as <c>kline.60.BTCUSDT</c> or <c>order</c>.</summary>
    public async Task WaitForTopicAsync(string topic, TimeSpan? timeout = null)
    {
        DateTime until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (!_connections.Values.Any(connection => connection.Topics.ContainsKey(topic)))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"No client subscribed to {topic}.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Pushes the data of a topic to every client subscribed to it.</summary>
    public async Task PushAsync(string topic, JsonArray data)
    {
        string message = new JsonObject
        {
            ["topic"] = topic,
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = "snapshot",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["creationTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["data"] = data
        }.ToJsonString();

        foreach (Connection connection in _connections.Values.Where(connection => connection.Topics.ContainsKey(topic)))
        {
            await connection.SendAsync(message);
        }
    }

    /// <summary>Breaks every connection, as a network failure does; clients reconnect and subscribe again.</summary>
    public async Task DropAsync()
    {
        foreach (WebSocket socket in _connections.Keys)
        {
            socket.Abort();
            _connections.TryRemove(socket, out _);
        }

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public static JsonObject Kline(DateTimeOffset openTime, TimeSpan interval, decimal close, bool confirm)
    {
        return new JsonObject
        {
            ["start"] = openTime.ToUnixTimeMilliseconds(),
            ["end"] = (openTime + interval).ToUnixTimeMilliseconds() - 1,
            ["interval"] = ((int)interval.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["open"] = Text(close),
            ["close"] = Text(close),
            ["high"] = Text(close),
            ["low"] = Text(close),
            ["volume"] = "1",
            ["turnover"] = Text(close),
            ["confirm"] = confirm,
            ["timestamp"] = (openTime + interval).ToUnixTimeMilliseconds()
        };
    }

    public static JsonObject Order(string category, string clientId, string status, decimal filled)
    {
        return new JsonObject
        {
            ["category"] = category,
            ["symbol"] = "BTCUSDT",
            ["orderId"] = $"x-{clientId}",
            ["orderLinkId"] = clientId,
            ["side"] = "Buy",
            ["orderType"] = "Limit",
            ["price"] = "100",
            ["qty"] = "1",
            ["timeInForce"] = "GTC",
            ["orderStatus"] = status,
            ["leavesQty"] = "0",
            ["cumExecQty"] = Text(filled),
            ["cumExecValue"] = Text(filled * 100m),
            ["cumExecFee"] = "0.02",
            ["feeCurrency"] = "USDT",
            ["rejectReason"] = "EC_NoError",
            ["createdTime"] = "1672364262444",
            ["updatedTime"] = "1672364262457"
        };
    }

    public static JsonObject Execution(string category, string clientId, string execId, decimal quantity, decimal price, decimal fee, string feeCoin, DateTimeOffset at)
    {
        return new JsonObject
        {
            ["category"] = category,
            ["symbol"] = "BTCUSDT",
            ["orderId"] = $"x-{clientId}",
            ["orderLinkId"] = clientId,
            ["side"] = "Buy",
            ["orderType"] = "Limit",
            ["orderPrice"] = "100",
            ["orderQty"] = "1",
            ["leavesQty"] = "0",
            ["execId"] = execId,
            ["execQty"] = Text(quantity),
            ["execPrice"] = Text(price),
            ["execFee"] = Text(fee),
            ["feeCurrency"] = feeCoin,
            ["execType"] = "Trade",
            ["execValue"] = "0",
            ["execTime"] = at.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isMaker"] = true,
            ["seq"] = 1
        };
    }

    private static string Text(decimal value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(socket, context.Request.Path.Value ?? "");
        _connections[socket] = connection;
        var buffer = new byte[64 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, context.RequestAborted);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await AnswerAsync(connection, JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))!.AsObject());
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // The connection broke: a drop of the test or the client went away.
        }
        finally
        {
            _connections.TryRemove(socket, out _);
        }
    }

    private async Task AnswerAsync(Connection connection, JsonObject request)
    {
        string op = request["op"]?.GetValue<string>() ?? "";
        string? requestId = request["req_id"]?.GetValue<string>();

        switch (op)
        {
            case "auth":
                Interlocked.Increment(ref _authentications);
                await connection.SendAsync(new JsonObject { ["success"] = true, ["ret_msg"] = "", ["op"] = "auth", ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString());

                break;
            case "subscribe":
                string[] topics = [.. request["args"]!.AsArray().Select(topic => topic!.GetValue<string>())];

                if (topics.FirstOrDefault(RefusedTopics.ContainsKey) is { } refused)
                {
                    await connection.SendAsync(
                        new JsonObject { ["success"] = false, ["ret_msg"] = $"error:handler not found,topic:{refused}", ["op"] = "subscribe", ["conn_id"] = "fake", ["req_id"] = requestId }
                            .ToJsonString());

                    break;
                }

                foreach (string topic in topics)
                {
                    connection.Topics[topic] = true;
                    _subscribes.AddOrUpdate(topic, 1, (_, count) => count + 1);
                }

                await connection.SendAsync(
                    new JsonObject { ["success"] = true, ["ret_msg"] = "", ["op"] = "subscribe", ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString());

                break;
            case "unsubscribe":
                foreach (JsonNode? topic in request["args"]!.AsArray())
                {
                    connection.Topics.TryRemove(topic!.GetValue<string>(), out _);
                }

                await connection.SendAsync(
                    new JsonObject { ["success"] = true, ["ret_msg"] = "", ["op"] = "unsubscribe", ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString());

                break;
            case "ping":
                await connection.SendAsync(new JsonObject { ["success"] = true, ["ret_msg"] = "pong", ["op"] = "ping", ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString());

                break;
        }
    }

    private sealed class Connection(WebSocket socket, string path)
    {
        private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);

        public string Path { get; } = path;

        public ConcurrentDictionary<string, bool> Topics { get; } = new ConcurrentDictionary<string, bool>();

        public async Task SendAsync(string message)
        {
            await _sending.WaitAsync();

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            finally
            {
                _sending.Release();
            }
        }
    }
}