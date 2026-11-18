using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProfitFox.Tests.Integration.Bench.MarketData;

/// <summary>
///     The fake market-data service in the test process, reached through an in-memory gRPC channel.
/// </summary>
public sealed class MarketDataServer : IAsyncDisposable
{
    private readonly IHost _host;

    public FakeMarketData Data { get; } = new FakeMarketData();

    public GrpcChannel Channel { get; }

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
                    });
                }))
            .StartAsync();
        GrpcChannel channel = GrpcChannel.ForAddress(
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
