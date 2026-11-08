using Bybit.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.IdeaTrades.Runtime;
using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.MarketData.FormingCandles;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Backfill;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Application.Scanner.Minute;
using ProfitFox.Application.Scanner.Universe;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Infrastructure.BackgroundServices;
using ProfitFox.Infrastructure.Exchanges;
using ProfitFox.Infrastructure.Exchanges.Bybit;
using ProfitFox.Infrastructure.Exchanges.Retry;
using ProfitFox.Infrastructure.Exchanges.Routing;
using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Infrastructure.Mediatr;
using ProfitFox.Infrastructure.Observability;
using ProfitFox.Infrastructure.Persistence.Postgres;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;
using ProfitFox.Infrastructure.Security;

namespace ProfitFox.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>The services of the API: it creates, cancels and reads work, and a Worker runs it.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        ConfigureCommon(configuration, services);
        MetricsRegistrar.Configure(configuration, services, "api");
        RequireMasterKey(configuration, services);
        services.AddHostedService<StrategyVersionsInitializationService>();

        // The feeds of the current candle are the API's alone (the broadcast they need is the hub's, registered by the API host).
        services.AddSingleton<FormingCandleFeeds>();
    }

    /// <summary>The services of a Worker: the lanes that take candle imports and backtest work from the database; it requires the master key.</summary>
    /// <exception cref="InvalidOperationException">
    ///     A lease of work or of a bot would expire before the Worker renews it, or a Worker in Development would trade on the
    ///     live Bybit.
    /// </exception>
    public static void ConfigureWorker(IConfiguration configuration, IServiceCollection services, IHostEnvironment environment)
    {
        WorkerSettings settings = Settings(configuration);

        if (settings.LeaseRenewal >= settings.LeaseDuration)
        {
            throw new InvalidOperationException("Worker:LeaseRenewal must be shorter than Worker:LeaseDuration.");
        }

        ConfigureCommon(configuration, services);
        MetricsRegistrar.Configure(configuration, services, "worker");
        TradingSettings trading = Trading(configuration);

        if (trading.Enabled && trading.LeaseRenewal >= trading.LeaseDuration)
        {
            throw new InvalidOperationException("Trading:LeaseRenewal must be shorter than Trading:LeaseDuration.");
        }

        // A local run never sends real orders.
        BybitSettings stub = configuration.GetSection("Bybit").Get<BybitSettings>() ?? new BybitSettings();

        if (trading.Enabled && environment.IsDevelopment() && (string.IsNullOrEmpty(stub.RestAddress) || string.IsNullOrEmpty(stub.SocketAddress)))
        {
            throw new InvalidOperationException(
                "A Worker in Development trades only against a stub of Bybit: set Bybit__RestAddress and Bybit__SocketAddress, or Trading__Enabled=false.");
        }

        // Every Worker decrypts secrets: a trading one the keys of the bots, any one the token of T-Invest for an import of moex (decision 12).
        RequireMasterKey(configuration, services);

        if (trading.Enabled)
        {
            services.AddHostedService<LiveTradingBackgroundService>();
            services.AddHostedService<IdeaAutoEntryBackgroundService>();
        }

        services.AddHostedService<WorkerStartLogService>();
        services.AddHostedService<CandleImportBackgroundService>();
        services.AddHostedService<BacktestBackgroundService>();
    }

    /// <summary>The services of the scanner: the data of the whole market, from public Bybit data only; no master key, no Worker lanes.</summary>
    /// <exception cref="InvalidOperationException">The lease of the stream would expire before the scanner renews it.</exception>
    public static void ConfigureScanner(IConfiguration configuration, IServiceCollection services)
    {
        ScannerSettings settings = configuration.GetSection("Scanner").Get<ScannerSettings>() ?? new ScannerSettings();

        if (settings.LeaseRenewal >= settings.LeaseDuration)
        {
            throw new InvalidOperationException("Scanner:LeaseRenewal must be shorter than Scanner:LeaseDuration.");
        }

        ConfigureCommon(configuration, services);
        MetricsRegistrar.Configure(configuration, services, "scanner");
        services.AddSingleton(settings);

        // Only the gate follows a change of the settings while the scanner runs; the rest read them once at the start.
        services.Configure<ScannerSettings>(configuration.GetSection("Scanner"));
        services.AddSingleton<ScannerRestGate>();
        services.AddSingleton<IMarketDataFeed, BybitPublicDataClient>();
        services.AddSingleton<IMarketStreams, BybitMarketStreams>();
        services.AddSingleton<IScannerJournal, LoggingScannerJournal>();
        services.AddScoped<ScannerMarket>();
        services.AddScoped<ScannerUniverse>();
        services.AddScoped<ScannerBackfill>();
        services.AddScoped<ScannerPace>();
        services.AddScoped<LiveAutos>();
        services.AddScoped<LiveIdeas>();
        services.AddScoped<IdeaStatistics>();
        services.AddScoped<IdeaRecomputes>();
        services.AddScoped<IdeasReplay>();
        services.AddScoped<ScannerMinuteCycle>();
        services.AddScoped<ScannerSession>();
        services.AddSingleton<ScannerMetrics>();
        services.AddHostedService<ScannerBackgroundService>();
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

    // A service without the slippage would count the ideas, the buckets or the size of a trade without it while the others count with it;
    // a backtest on a category without base rates would have no fees to count with (decision 9).
    private static void RequireIdeaSlippage(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<BacktestSettings>()
            .Bind(configuration.GetSection("Backtests"))
            .Validate(
                settings => settings.CategoriesWithoutFees().Count == 0,
                "Backtests:DefaultFees must have Limit and Market for every market category: spot, linear and moex.")
            .Validate(
                settings => settings.HasIdeaSlippage(),
                "Backtests:DefaultFees:spot:Slippage and Backtests:DefaultFees:linear:Slippage must be set, in percent and not below zero: the ideas count the slippage by them.")
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

        // Every client of the factory, Bybit.Net's own included, counts its attempts to Bybit REST; other hosts pass uncounted.
        services.AddSingleton<BybitLimitHeadroom>();
        services.AddTransient<BybitRestMetricsHandler>();
        services.ConfigureHttpClientDefaults(builder => builder.AddHttpMessageHandler<BybitRestMetricsHandler>());
        services.AddHttpClient<IExchangeKeyInspector, BybitAccountClient>();
        services.AddHttpClient<IExchangeLedger, BybitLedger>();
        services.AddHttpClient<IOrderGateway, BybitOrderGateway>();
        services.AddSingleton<BybitStreams>();
        services.AddSingleton<IExchangeStreams>(provider => provider.GetRequiredService<BybitStreams>());
        services.AddSingleton<IFormingCandleStreams>(provider => provider.GetRequiredService<BybitStreams>());
        services.AddSingleton<ILiveTradingJournal, LoggingLiveTradingJournal>();

        // A circuit breaker per exchange: a T-Invest that does not answer never opens the circuit of Bybit.
        services.AddKeyedSingleton<ExchangeResilienceExecutor>(Exchange.Bybit);
        services.AddKeyedSingleton<ExchangeResilienceExecutor>(Exchange.TInvest);
        ConfigureTInvest(configuration, services);

        // The read ports go to the exchange of the category (decision 8): Bybit for spot and linear, T-Invest for moex.
        services.AddKeyedTransient<IMarketHistoryClient, BybitMarketDataClient>(Exchange.Bybit);
        services.AddKeyedTransient<IMarketHistoryClient, TInvestMarketDataClient>(Exchange.TInvest);
        services.AddTransient<IMarketHistoryClient, RoutedMarketHistoryClient>();
        services.AddKeyedTransient<IInstrumentRulesProvider, BybitMarketDataClient>(Exchange.Bybit);
        services.AddKeyedTransient<IInstrumentRulesProvider, TInvestMarketDataClient>(Exchange.TInvest);
        services.AddTransient<IInstrumentRulesProvider, RoutedInstrumentRulesProvider>();
        services.AddKeyedTransient<IInstrumentCatalog, BybitMarketDataClient>(Exchange.Bybit);
        services.AddKeyedTransient<IInstrumentCatalog, TInvestMarketDataClient>(Exchange.TInvest);
        services.AddKeyedTransient<IInstrumentCatalog, RoutedInstrumentCatalog>(CachedInstrumentCatalog.InnerKey);
        services.AddTransient<CandleReadThrough>();
        services.AddTransient<IdeaTradeEntryCalculator>();
        services.AddSingleton<IInstrumentCatalog>(provider => new CachedInstrumentCatalog(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(configuration.GetSection("Backtests").Get<BacktestSettings>() ?? new BacktestSettings());
        RequireIdeaSlippage(configuration, services);
        services.AddSingleton(configuration.GetSection("Ideas").Get<IdeasSettings>() ?? new IdeasSettings());
        services.AddSingleton(new EngineVersion(EngineVersion.Current));
        services.AddSingleton(new IdeasEngineVersion(IdeasEngineVersion.Current));
        services.AddSingleton(configuration.GetSection("ExchangeAccounts").Get<ExchangeAccountSettings>() ?? new ExchangeAccountSettings());
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        // MediatR registers the Worker's handlers in the API too, so what they take must resolve there as well.
        services.AddSingleton(Settings(configuration));
        services.AddSingleton(Trading(configuration));
        services.AddSingleton(new WorkerIdentity(Guid.NewGuid()));

        // One loop for all bots of the Worker; it reads and renews with a connection per call, outside any unit of work.
        services.AddSingleton(provider => new LiveBotLeases(
            new LiveBotRepository(provider.GetRequiredService<NpgsqlDataSource>(), new PostgresTransactionAccessor()),
            provider.GetRequiredService<TradingSettings>(),
            provider.GetRequiredService<WorkerSettings>(),
            provider.GetRequiredService<WorkerIdentity>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new IdeaTradeLeases(
            new IdeaTradeRepository(provider.GetRequiredService<NpgsqlDataSource>(), new PostgresTransactionAccessor()),
            provider.GetRequiredService<TradingSettings>(),
            provider.GetRequiredService<WorkerSettings>(),
            provider.GetRequiredService<WorkerIdentity>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<BacktestInputCache>();
    }

    /// <summary>
    ///     The adapter of T-Invest (decisions 3 and 7): one channel of the process, one limiter and one token source, the clients of
    ///     market data and of accounts over them. Nothing connects until a call, and no call goes without a T-Invest account in storage.
    /// </summary>
    /// <exception cref="InvalidOperationException">The endpoint is not the official host, or a certificate file does not match the settings.</exception>
    private static void ConfigureTInvest(IConfiguration configuration, IServiceCollection services)
    {
        TInvestSettings tinvest = configuration.GetSection("TInvest").Get<TInvestSettings>() ?? new TInvestSettings();

        if (tinvest.Problem() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        services.AddSingleton(tinvest);
        services.AddSingleton(new MoexTradingSettings(tinvest.Trading));
        services.AddSingleton(TInvestCertificates.Load(tinvest.Certificates, AppContext.BaseDirectory));
        services.AddSingleton<ITInvestCallInvokers, TInvestChannel>();
        services.AddSingleton<TInvestRequestLimiter>();
        services.AddSingleton<TInvestMetrics>();
        services.AddSingleton<TInvestTokenSource>();
        services.AddSingleton<TInvestShares>();
        services.AddTransient<TInvestCall>();
        services.AddTransient<TInvestMarketDataClient>();
        services.AddTransient<IBrokerAccountReader, TInvestAccountClient>();
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

    private static TradingSettings Trading(IConfiguration configuration)
    {
        return configuration.GetSection("Trading").Get<TradingSettings>() ?? new TradingSettings();
    }
}