using Grpc.Net.Client;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Infrastructure.Clients.MarketData;

internal static class MarketDataRegistrar
{
    /// <summary>
    ///     The market-data service: the <c>History</c> streams and the live feeds over gRPC (<c>MarketData:GrpcUrl</c>), the
    ///     instrument catalog and recent history over REST (<c>MarketData:RestUrl</c>). A test host replaces the ports by
    ///     registering its own afterwards.
    /// </summary>
    /// <exception cref="InvalidOperationException">An address is not set.</exception>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        var grpc = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:GrpcUrl"));
        var rest = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:RestUrl"));
        services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        services.AddSingleton<IHistorySource>(provider => new GrpcHistorySource(provider.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IInstrumentCatalog>(_ => new InstrumentCatalog(new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) }));
        services.AddSingleton<IRecentHistory>(provider => new RestRecentHistory(
            new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) },
            provider.GetRequiredService<GrpcChannel>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton(new MarketFeedSettings());
        services.AddSingleton<IMarketFeed>(provider => new GrpcMarketFeed(
            provider.GetRequiredService<GrpcChannel>(),
            provider.GetRequiredService<IInstrumentCatalog>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<MarketFeedSettings>()));
    }
}
