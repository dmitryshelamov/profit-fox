using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration;

/// <summary>
///     The services of the bench as <c>AddInfrastructure</c> gives them, over the migrated test database, for the tests that send
///     the commands and queries of the application without a host. The market-data service is the fake one of a test, or none.
/// </summary>
internal static class BenchServices
{
    private static readonly Lazy<ServiceProvider> Shared = new Lazy<ServiceProvider>(() => Start());

    /// <summary>
    ///     The sender of the test database with no market-data service, one for the test assembly: for the tests that only load
    ///     hypotheses and read or queue runs.
    /// </summary>
    public static ISender Sender => Shared.Value.GetRequiredService<ISender>();

    /// <summary>
    ///     A service of the shared services, such as a repository, for the tests of the store itself.
    /// </summary>
    public static T Get<T>()
        where T : notnull
    {
        return Shared.Value.GetRequiredService<T>();
    }

    /// <summary>
    ///     The services over <paramref name="connectionString" /> (the test database when null), reading history from
    ///     <paramref name="market" /> when it is given; with <paramref name="time" /> the clock of the services and of the fake
    ///     service is that one; with <paramref name="cache" /> history goes through the cache of history in that folder, as
    ///     <c>MarketData:HistoryCache:Path</c> makes it (change add-backtest-history-cache). A dropped read is repeated without
    ///     a pause. With <paramref name="log" /> the journal of the services goes there too.
    /// </summary>
    public static ServiceProvider Start(MarketDataServer? market = null, string? connectionString = null, TimeProvider? time = null, string? cache = null, ListLogger? log = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (log is not null)
            {
                logging.AddProvider(new ListLoggerProvider(log));
            }
        });
        services.AddInfrastructure(BenchDatabase.Settings(connectionString ?? BenchDatabase.ConnectionString));
        services.AddSingleton(Repeats.WithoutPauses);
        if (market is not null)
        {
            MarketDataRegistrar.History(
                services,
                cache is null ? null : new HistoryCacheSettings(cache, 1L << 40),
                _ => new GrpcHistorySource(market.Channel));
            services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
            services.AddSingleton<IRecentHistory>(provider => new RestRecentHistory(market.Http, market.Channel, provider.GetService<TimeProvider>() ?? TimeProvider.System));
            services.AddSingleton<IMarketFeed>(provider => new GrpcMarketFeed(market.Channel, new InstrumentCatalog(market.Http), provider.GetService<TimeProvider>() ?? TimeProvider.System, new MarketFeedSettings { RetryDelay = TimeSpan.FromMilliseconds(50) }));
        }

        if (time is not null)
        {
            services.AddSingleton(time);
            if (market is not null)
            {
                market.Data.Time = time;
            }
        }

        return services.BuildServiceProvider();
    }
}
