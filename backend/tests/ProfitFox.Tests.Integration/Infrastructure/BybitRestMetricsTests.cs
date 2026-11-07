using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using Shouldly;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Decision 2: the counter sits on every client of the factory, the shared Bybit client and the clients with an account key alike.</summary>
public class BybitRestMetricsTests
{
    // The shared client keeps Bybit.Net's own primary handler and reaches a listener; the account client is answered by the fake.
    [Test]
    public async Task Shared_and_account_clients_are_counted()
    {
        using var listener = new HttpListener();
        string address = Prefix(listener);
        listener.Start();
        Task serving = ServeInstrumentsAsync(listener);

        await using var factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString)
        {
            Workers = 0,
            FakeBybit = false,
            FakeKeyInspector = false,
            BybitRestAddress = address
        };
        using HttpClient client = factory.CreateClient();
        using var requests = new MetricCollector<long>(factory.Services.GetRequiredService<IMeterFactory>(), "ProfitFox.Bybit", "profitfox.bybit.rest.requests");

        (await client.GetAsync("/api/market-data/instruments?category=spot&query=btc")).StatusCode.ShouldBe(HttpStatusCode.OK);
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/exchange-accounts",
            new JsonObject { ["name"] = "Основной", ["apiKey"] = "api-key", ["apiSecret"] = "secret" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        IEnumerable<object?> endpoints = requests.GetMeasurementSnapshot().Select(measurement => measurement.Tags["bybit.endpoint"]);
        endpoints.ShouldContain("/v5/market/instruments-info");
        endpoints.ShouldContain("/v5/user/query-api");

        listener.Stop();
        await serving;
    }

    private static string Prefix(HttpListener listener)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var address = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(address + "/");

        return address;
    }

    private static async Task ServeInstrumentsAsync(HttpListener listener)
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

            byte[] body = Encoding.UTF8.GetBytes(
                """{"retCode":0,"retMsg":"OK","result":{"category":"spot","list":[{"symbol":"BTCUSDT","baseCoin":"BTC","quoteCoin":"USDT","status":"Trading"}]},"retExtInfo":{},"time":0}""");
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}