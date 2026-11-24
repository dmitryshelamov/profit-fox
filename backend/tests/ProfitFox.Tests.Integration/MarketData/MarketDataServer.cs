using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The fake market-data service in the test process, reached through an in-memory gRPC channel.
/// </summary>
public sealed class MarketDataServer : IAsyncDisposable
{
    private readonly IHost _host;

    public FakeMarketData Data { get; } = new FakeMarketData();

    public GrpcChannel Channel { get; }

    /// <summary>
    ///     The catalog the fake service lists: three linear USDT perpetuals and a spot pair.
    /// </summary>
    public const string Catalog =
        """
        [
          { "id": "btc", "listing": { "category": "Linear", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "eth", "listing": { "category": "Linear", "symbol": "ETHUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "sol", "listing": { "category": "Linear", "symbol": "SOLUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": "LinearPerpetual" }, "delistedAt": null },
          { "id": "spot-btc", "listing": { "category": "Spot", "symbol": "BTCUSDT", "quoteAsset": "USDT", "status": "Trading", "contractType": null }, "delistedAt": null }
        ]
        """;

    public HttpClient Http => _host.GetTestClient();

    private MarketDataServer(IHost host, GrpcChannel channel, FakeMarketData data)
    {
        _host = host;
        Channel = channel;
        Data = data;
    }

    public static async Task<MarketDataServer> Start()
    {
        var data = new FakeMarketData();
        IHost host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddGrpc();
                    services.AddSingleton(data);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<FakeCandleFeed>();
                        endpoints.MapGrpcService<FakeFundingFeed>();
                        endpoints.MapGrpcService<FakeFundingRateFeed>();
                        endpoints.MapGrpcService<FakeOpenInterestFeed>();
                        endpoints.MapGet("/api/instruments/bybit", () => Results.Text(data.Catalog, "application/json"));
                        endpoints.MapGet("/api/instruments/{id}/candles", (HttpRequest request, string id) => FakeRecentHistory.Candles(data, request, id));
                        endpoints.MapGet("/api/instruments/{id}/funding-payments", (HttpRequest request, string id) => FakeRecentHistory.Funding(data, request, id));
                        endpoints.MapGet("/api/instruments/{id}/open-interest", (HttpRequest request, string id) => FakeRecentHistory.OpenInterest(data, request, id));
                    });
                }))
            .StartAsync();
        var channel = GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions { HttpHandler = host.GetTestServer().CreateHandler() });
        return new MarketDataServer(host, channel, data);
    }

    public async ValueTask DisposeAsync()
    {
        Channel.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
