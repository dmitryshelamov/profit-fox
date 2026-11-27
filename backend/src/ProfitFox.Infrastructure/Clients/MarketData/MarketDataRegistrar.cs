using System.Globalization;

using Grpc.Net.Client;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Services.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;

namespace ProfitFox.Infrastructure.Clients.MarketData;

internal static class MarketDataRegistrar
{
    /// <summary>
    ///     The market-data service: the <c>History</c> streams and the live feeds over gRPC (<c>MarketData:GrpcUrl</c>), the
    ///     instrument catalog and recent history over REST (<c>MarketData:RestUrl</c>). A test host replaces the ports by
    ///     registering its own afterwards. The queue of the live feeds is <c>Scanner:QueueCapacity</c> long, as that of the
    ///     scanner, and the stream of minutes may be silent for <c>Scanner:FeedSilence</c>, as the scanner counts it. History
    ///     goes through the cache in <c>MarketData:HistoryCache:Path</c> when it is set (see <see cref="History" />); the
    ///     recent history of the scanner never does.
    /// </summary>
    /// <exception cref="InvalidOperationException">An address is not set, or the limit of the cache is not above zero.</exception>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        var grpc = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:GrpcUrl"));
        var rest = new Uri(InfrastructureRegistrar.Setting(configuration, "MarketData:RestUrl"));
        services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { HttpHandler = Handler(), MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        History(services, HistoryCache(configuration), provider => new GrpcHistorySource(provider.GetRequiredService<GrpcChannel>()));
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
    ///     The source of history of the process (change add-backtest-history-cache, design.md, decision 1): the
    ///     market-data service of <paramref name="service" />, through the cache of <paramref name="cache" /> when there is one,
    ///     and the counters of that cache. A test registers its fake service of history with it.
    /// </summary>
    internal static void History(IServiceCollection services, HistoryCacheSettings? cache, Func<IServiceProvider, IHistorySource> service)
    {
        if (cache is null)
        {
            services.AddSingleton<IHistorySource>(service);
            services.AddSingleton<IHistoryCacheCounters>(new NoHistoryCache());
            return;
        }

        services.AddSingleton(provider =>
        {
            TimeProvider time = provider.GetService<TimeProvider>() ?? TimeProvider.System;
            return new CachingHistorySource(
                service(provider),
                new FileHistoryMonthStore(cache.Path, cache.MaxBytes, time),
                time,
                provider.GetService<ILoggerFactory>()?.CreateLogger<CachingHistorySource>() ?? (ILogger)NullLogger.Instance);
        });
        services.AddSingleton<IHistorySource>(provider => provider.GetRequiredService<CachingHistorySource>());
        services.AddSingleton<IHistoryCacheCounters>(provider => provider.GetRequiredService<CachingHistorySource>());
    }

    /// <summary>
    ///     The cache of history of the settings (decision 10): the folder <c>MarketData:HistoryCache:Path</c>, where
    ///     <c>~/</c> is the home folder of the user, and the limit <c>MarketData:HistoryCache:MaxSizeGb</c>, 40 GB when not
    ///     set; null when the folder is not set or empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">The limit is not a number above zero.</exception>
    internal static HistoryCacheSettings? HistoryCache(IConfiguration configuration)
    {
        const string LimitKey = "MarketData:HistoryCache:MaxSizeGb";
        double gigabytes = 40;
        if (configuration[LimitKey] is { Length: > 0 } limit
            && (!double.TryParse(limit, NumberStyles.Float, CultureInfo.InvariantCulture, out gigabytes) || !(gigabytes > 0) || double.IsInfinity(gigabytes)))
        {
            throw new InvalidOperationException($"The setting {LimitKey} is {limit}, not a number of gigabytes above zero.");
        }

        if (configuration["MarketData:HistoryCache:Path"] is not { Length: > 0 } path)
        {
            return null;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return new HistoryCacheSettings(Path.GetFullPath(path), (long)(gigabytes * 1024 * 1024 * 1024));
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
