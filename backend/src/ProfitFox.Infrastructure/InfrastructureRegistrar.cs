using Bybit.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Exchanges;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Security;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>The services of the API: it creates, cancels and reads work, and a Worker runs it.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        ConfigureCommon(configuration, services);
        RequireMasterKey(configuration, services);
        services.AddHostedService<StrategyVersionsInitializationService>();
    }

    /// <summary>The services of a Worker: the lanes that take candle imports and backtest work from the database.</summary>
    /// <exception cref="InvalidOperationException">
    ///     The lease would expire before the Worker renews it, or a Worker in Development would trade on the live Bybit.
    /// </exception>
    public static void ConfigureWorker(IConfiguration configuration, IServiceCollection services, IHostEnvironment environment)
    {
        WorkerSettings settings = Settings(configuration);

        if (settings.LeaseRenewal >= settings.LeaseDuration)
        {
            throw new InvalidOperationException("Worker:LeaseRenewal must be shorter than Worker:LeaseDuration.");
        }

        ConfigureCommon(configuration, services);
        TradingSettings trading = configuration.GetSection("Trading").Get<TradingSettings>() ?? new TradingSettings();
        services.AddSingleton(trading);

        // A local run never sends real orders.
        BybitSettings stub = configuration.GetSection("Bybit").Get<BybitSettings>() ?? new BybitSettings();

        if (trading.Enabled && environment.IsDevelopment() && (string.IsNullOrEmpty(stub.RestAddress) || string.IsNullOrEmpty(stub.SocketAddress)))
        {
            throw new InvalidOperationException(
                "A Worker in Development trades only against a stub of Bybit: set Bybit__RestAddress and Bybit__SocketAddress, or Trading__Enabled=false.");
        }

        // The API and a trading Worker decrypt secrets; a Worker that only runs backtests never needs the key (decision 12).
        if (trading.Enabled)
        {
            RequireMasterKey(configuration, services);
            services.AddHostedService<LiveTradingBackgroundService>();
        }

        services.AddHostedService<WorkerStartLogService>();
        services.AddHostedService<CandleImportBackgroundService>();
        services.AddHostedService<BacktestBackgroundService>();
    }

    private static void RequireMasterKey(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<ExchangeAccountSettings>()
            .Bind(configuration.GetSection("ExchangeAccounts"))
            .Validate(
                settings => IsMasterKey(settings.MasterKey),
                "ExchangeAccounts__MasterKey must be set to 32 random bytes in base64, such as the output of `openssl rand -base64 32`.")
            .ValidateOnStart();
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
        services.AddSingleton(bybit);
        services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>();
        services.AddHttpClient<IExchangeLedger, BybitLedger>();
        services.AddHttpClient<IOrderGateway, BybitOrderGateway>();
        services.AddSingleton<IExchangeStreams, BybitStreams>();
        services.AddSingleton<ILiveTradingJournal, LoggingLiveTradingJournal>();
        services.AddSingleton<ExchangeResilienceExecutor>();
        services.AddTransient<IMarketHistoryClient, BybitMarketDataClient>();
        services.AddTransient<IInstrumentRulesProvider, BybitMarketDataClient>();
        services.AddKeyedTransient<IInstrumentCatalog, BybitMarketDataClient>(CachedInstrumentCatalog.InnerKey);
        services.AddSingleton<IInstrumentCatalog>(provider => new CachedInstrumentCatalog(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(configuration.GetSection("Backtests").Get<BacktestSettings>() ?? new BacktestSettings());
        services.AddSingleton(new EngineVersion(EngineVersion.Current));
        services.AddSingleton(configuration.GetSection("ExchangeAccounts").Get<ExchangeAccountSettings>() ?? new ExchangeAccountSettings());
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        // MediatR registers the Worker's handlers in the API too, so what they take must resolve there as well.
        services.AddSingleton(Settings(configuration));
        services.AddSingleton(new WorkerIdentity(Guid.NewGuid()));
        services.AddSingleton<BacktestInputCache>();
    }

    private static bool IsMasterKey(string? value)
    {
        var key = new byte[32];

        return Convert.TryFromBase64String(value ?? string.Empty, key, out int length) && length == key.Length;
    }

    private static WorkerSettings Settings(IConfiguration configuration)
    {
        return configuration.GetSection("Worker").Get<WorkerSettings>() ?? new WorkerSettings();
    }
}