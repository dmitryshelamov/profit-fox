using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Bybit.Net;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Design decision 10: <c>Bybit:RestAddress</c> points the REST client at another address; without it the client is live.</summary>
public class BybitAddressTests
{
    [Test]
    public async Task Configured_address_receives_the_instruments_request()
    {
        using var listener = new HttpListener();
        var address = Prefix(listener);
        listener.Start();
        var requests = new List<string>();
        Task serving = ServeAsync(listener, requests);

        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync())
        {
            Workers = 0,
            FakeBybit = false,
            BybitRestAddress = address
        };
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/market-data/instruments?category=spot&query=btc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] found = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;
        found.Select(instrument => instrument.GetProperty("symbol").GetString()).ShouldBe(["BTCUSDT"]);
        requests.ShouldContain(path => path.StartsWith("/v5/market/instruments-info?category=spot", StringComparison.Ordinal));

        listener.Stop();
        await serving;
    }

    [Test]
    public async Task Without_the_setting_the_environment_is_live()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0, FakeBybit = false };
        factory.CreateClient().Dispose();

        BybitRestOptions options = factory.Services.GetRequiredService<IOptions<BybitRestOptions>>().Value;

        options.Environment.RestBaseAddress.ShouldBe(BybitEnvironment.Live.RestBaseAddress);
    }

    [Test]
    public async Task Empty_setting_means_the_environment_is_live()
    {
        await using var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0, FakeBybit = false, BybitRestAddress = "" };
        factory.CreateClient().Dispose();

        BybitRestOptions options = factory.Services.GetRequiredService<IOptions<BybitRestOptions>>().Value;

        options.Environment.RestBaseAddress.ShouldBe(BybitEnvironment.Live.RestBaseAddress);
    }

    private static string Prefix(HttpListener listener)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string address = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(address + "/");

        return address;
    }

    private static async Task ServeAsync(HttpListener listener, List<string> requests)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            requests.Add(context.Request.Url!.PathAndQuery);
            byte[] body = Encoding.UTF8.GetBytes(
                """{"retCode":0,"retMsg":"OK","result":{"category":"spot","list":[{"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"Trading"},{"symbol":"SOLUSDT","baseCoin":"SOL","quoteCoin":"USDT","status":"Trading"}]},"retExtInfo":{},"time":0}""");
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}