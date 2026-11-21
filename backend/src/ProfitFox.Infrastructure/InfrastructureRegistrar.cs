using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Queries.Preview;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Persistence.Postgres;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     MediatR with the handlers of Application, the market-data service, the database of the bench with its repositories and
    ///     migrations, the clock and the memory of the preview, one for the process. A setting that is not there stops the start
    ///     here, before anything runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A setting is not set.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MediatrRegistrar.Configure(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PreviewHistoryCache>();

        MarketDataRegistrar.Configure(configuration, services);

        PostgresRegistrar.Configure(configuration, services);

        return services;
    }

    /// <summary>
    ///     The value of a setting that must be there.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting is not set or is empty.</exception>
    internal static string Setting(IConfiguration configuration, string key)
    {
        return configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The setting {key} is not set.");
    }
}
