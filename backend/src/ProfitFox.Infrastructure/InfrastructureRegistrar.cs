using Bybit.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Exchanges;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Persistence.Postgres;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>The services of the API: it creates, cancels and reads work, and a Worker runs it.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        ConfigureCommon(configuration, services);
        services.AddHostedService<StrategyVersionsInitializationService>();
    }

    /// <summary>The services of a Worker: the lanes that take candle imports and backtest work from the database.</summary>
    /// <exception cref="InvalidOperationException">The lease would expire before the Worker renews it.</exception>
    public static void ConfigureWorker(IConfiguration configuration, IServiceCollection services)
    {
        WorkerSettings settings = Settings(configuration);

        if (settings.LeaseRenewal >= settings.LeaseDuration)
        {
            throw new InvalidOperationException("Worker:LeaseRenewal must be shorter than Worker:LeaseDuration.");
        }

        ConfigureCommon(configuration, services);
        services.AddHostedService<WorkerStartLogService>();
        services.AddHostedService<CandleImportBackgroundService>();
        services.AddHostedService<BacktestBackgroundService>();
    }

    private static void ConfigureCommon(IConfiguration configuration, IServiceCollection services)
    {
        MediatrRegistrar.Configure(services);
        PostgresRegistrar.Configure(configuration, services);

        services.AddSingleton(TimeProvider.System);
        BybitSettings bybit = configuration.GetSection("Bybit").Get<BybitSettings>() ?? new BybitSettings();
        services.AddBybit(options =>
        {
            if (!string.IsNullOrEmpty(bybit.RestAddress))
            {
                options.Rest.Environment = BybitEnvironment.CreateCustom("profit-fox", bybit.RestAddress, BybitEnvironment.Live.SocketBaseAddress);
            }
        });
        services.AddSingleton<ExchangeResilienceExecutor>();
        services.AddTransient<IMarketHistoryClient, BybitMarketDataClient>();
        services.AddTransient<IInstrumentRulesProvider, BybitMarketDataClient>();
        services.AddKeyedTransient<IInstrumentCatalog, BybitMarketDataClient>(CachedInstrumentCatalog.InnerKey);
        services.AddSingleton<IInstrumentCatalog>(provider => new CachedInstrumentCatalog(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(configuration.GetSection("Backtests").Get<BacktestSettings>() ?? new BacktestSettings());
        services.AddSingleton(new EngineVersion(EngineVersion.Current));

        // MediatR registers the Worker's handlers in the API too, so what they take must resolve there as well.
        services.AddSingleton(Settings(configuration));
        services.AddSingleton(new WorkerIdentity(Guid.NewGuid()));
        services.AddSingleton<BacktestInputCache>();
    }

    private static WorkerSettings Settings(IConfiguration configuration)
    {
        return configuration.GetSection("Worker").Get<WorkerSettings>() ?? new WorkerSettings();
    }
}