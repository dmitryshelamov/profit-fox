using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Tests.Integration.MarketData;

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
    ///     service is that one.
    /// </summary>
    public static ServiceProvider Start(MarketDataServer? market = null, string? connectionString = null, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(BenchDatabase.Settings(connectionString ?? BenchDatabase.ConnectionString));
        if (market is not null)
        {
            services.AddSingleton<IHistorySource>(new GrpcHistorySource(market.Channel));
            services.AddSingleton<IInstrumentCatalog>(new InstrumentCatalog(market.Http));
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
