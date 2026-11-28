using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Queries.Preview;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.Market;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Infrastructure.Clients.Bybit;
using ProfitFox.Infrastructure.Clients.MarketData;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Security;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     MediatR with the handlers of Application, the market-data service, the database of the bench with its repositories and
    ///     migrations, the reader of Bybit and the protector of the secrets of the accounts, the clock, the repeats of a dropped
    ///     read of a backtest, the memory of the preview, one for the process, and the default settings of the scanner of
    ///     ideas, which the Worker replaces by its own. A setting that is not there stops the start here, before anything runs.
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
        services.AddSingleton(new TradingSettings());
        services.AddTransient<IdeaLibrary>();
        services.AddTransient<EntryContextReader>();
        services.AddSingleton<LiveCandleHub>();
        // The limit of the reads of the page of a pair, one for the process: the journal tells of each step once a minute.
        services.AddSingleton<PageReads>();
        services.AddSingleton<ChartPairs>();

        MarketDataRegistrar.Configure(configuration, services);

        PostgresRegistrar.Configure(configuration, services);

        // The accounts of the exchange (change add-exchange-accounts): the reader of Bybit and the protector of the secrets,
        // which reads the master key only when it is used.
        BybitRegistrar.Configure(configuration, services);
        SecurityRegistrar.Configure(configuration, services);

        return services;
    }

    /// <summary>
    ///     The check of the master key <c>ExchangeAccounts:MasterKey</c> when the host starts (change add-exchange-accounts,
    ///     design.md, decisions 1 and 2): without it, or with a key that is not 32 bytes in base64, the host does not start.
    ///     The API calls it, and the trading Worker (change take-idea-trades, design.md, decision 2); the other Workers start
    ///     without the master key and never decrypt a secret.
    /// </summary>
    public static IServiceCollection RequireMasterKey(this IServiceCollection services)
    {
        SecurityRegistrar.RequireAtStart(services);
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
