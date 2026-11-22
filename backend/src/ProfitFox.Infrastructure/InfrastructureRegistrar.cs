using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Persistence.Postgres;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     MediatR with the handlers of Application, the market-data service, the database of the bench with its repositories and
    ///     migrations, the clock, the repeats of a dropped read of a backtest, the memory of the preview, one for the process, and
    ///     the default settings of the scanner of ideas, which the Worker replaces by its own. A
    ///     setting that is not there stops the start here, before anything runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A setting is not set.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MediatrRegistrar.Configure(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HistoryRepeats());
        services.AddSingleton<PreviewHistoryCache>();
        services.AddSingleton(new ScannerSettings());
        services.AddSingleton(new IdeasSettings());
        services.AddTransient<IdeaLibrary>();

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
