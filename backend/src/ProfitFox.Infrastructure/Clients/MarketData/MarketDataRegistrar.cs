using System.Globalization;

using Grpc.Net.Client;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Infrastructure.Clients.MarketData;

internal static class MarketDataRegistrar
{
    /// <summary>
    ///     The market-data service: the <c>History</c> streams and the live feeds over gRPC (<c>MarketData:GrpcUrl</c>), the
    ///     instrument catalog and recent history over REST (<c>MarketData:RestUrl</c>). A test host replaces the ports by
    ///     registering its own afterwards. The queue of the live feeds is <c>Scanner:QueueCapacity</c> long, as that of the
    ///     scanner, and the stream of minutes may be silent for <c>Scanner:FeedSilence</c>, as the scanner counts it.
    /// </summary>
    /// <exception cref="InvalidOperationException">An address is not set.</exception>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        var grpc = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:GrpcUrl"));
        var rest = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:RestUrl"));
        services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { HttpHandler = Handler(), MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        services.AddSingleton<IHistorySource>(provider => new GrpcHistorySource(provider.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IInstrumentCatalog>(_ => new InstrumentCatalog(new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) }));
        services.AddSingleton<IRecentHistory>(provider => new RestRecentHistory(
            new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) },
            provider.GetRequiredService<GrpcChannel>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<ILoggerFactory>()?.CreateLogger<RestRecentHistory>()));
        // One setting for both queues between the feed and the scanner (task 19.8): Scanner:QueueCapacity.
        var feed = new MarketFeedSettings();
        if (int.TryParse(configuration["Scanner:QueueCapacity"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int capacity))
        {
            feed = feed with { QueueCapacity = capacity };
        }

        // One setting for the watchdog of the feed and the state of the scanner (task 24.1): Scanner:FeedSilence.
        if (TimeSpan.TryParse(configuration["Scanner:FeedSilence"], CultureInfo.InvariantCulture, out TimeSpan silence))
        {
            feed = feed with { FeedSilence = silence };
        }

        services.AddSingleton(feed);
        services.AddSingleton<IMarketFeed>(provider => new GrpcMarketFeed(
            provider.GetRequiredService<GrpcChannel>(),
            provider.GetRequiredService<IInstrumentCatalog>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<MarketFeedSettings>(),
            provider.GetService<ILoggerFactory>()?.CreateLogger<GrpcMarketFeed>()));
    }

    /// <summary>
    ///     The handler of the channel (task 24.1 (б)): the connection is pinged every 30 seconds, also with no call open, and
    ///     dropped when no answer came in 10 seconds, so a half-open connection fails the calls on it instead of leaving them
    ///     waiting. Several HTTP/2 connections are allowed, as in the handler <c>GrpcChannel</c> makes by itself.
    /// </summary>
    internal static SocketsHttpHandler Handler()
    {
        return new SocketsHttpHandler
        {
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            EnableMultipleHttp2Connections = true
        };
    }
}
