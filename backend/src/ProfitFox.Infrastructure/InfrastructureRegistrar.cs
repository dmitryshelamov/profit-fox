using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Messaging;
using ProfitFox.Infrastructure.Persistence.Postgres;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        MediatrRegistrar.Configure(services);
        PostgresRegistrar.Configure(configuration, services);

        services.AddSingleton(TimeProvider.System);
        services.AddBybit();
        services.AddSingleton<ExchangeResilienceExecutor>();
        services.AddTransient<IMarketHistoryClient, BybitMarketDataClient>();

        services.AddHostedService<StrategyVersionsInitializationService>();

        services.AddSingleton<CandleImportQueue>();
        services.AddSingleton<ICandleImportQueue>(provider => provider.GetRequiredService<CandleImportQueue>());
        services.AddHostedService<CandleImportInitializationService>();
        services.AddHostedService<CandleImportBackgroundService>();
    }
}