using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.LiveTrading;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.SavedFilters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres;

internal static class PostgresRegistrar
{
    /// <summary>The connection string is read on first use, so a test host can still replace it after registration.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(ConnectionString(configuration)));
        services.AddScoped<ICandleRepository, CandleRepository>();
        services.AddScoped<ICandleImportJobRepository, CandleImportJobRepository>();
        services.AddScoped<IStrategyLibraryRepository, StrategyLibraryRepository>();
        services.AddScoped<IBacktestRepository, BacktestRepository>();
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<PostgresTransactionAccessor>();
        services.AddScoped<ITransactionExecutor, PostgresTransactionExecutor>();
        services.AddScoped<ILiveBotRepository, LiveBotRepository>();
        services.AddScoped<IIdeaTradeRepository, IdeaTradeRepository>();
        services.AddScoped<IIdeaTradingSettingsRepository, IdeaTradingSettingsRepository>();
        services.AddScoped<IScannerPairRepository, ScannerPairRepository>();
        services.AddScoped<IOpenInterestRepository, OpenInterestRepository>();
        services.AddScoped<IFundingRateRepository, FundingRateRepository>();
        services.AddScoped<IMarketSnapshotRepository, MarketSnapshotRepository>();
        services.AddScoped<IIdeaRepository, IdeaRepository>();
        services.AddScoped<IIdeaAutoRepository, IdeaAutoRepository>();
        services.AddScoped<IPaperIdeaTradeRepository, PaperIdeaTradeRepository>();
        services.AddScoped<IIdeaAutoEntryRepository, IdeaAutoEntryRepository>();
        services.AddScoped<IIdeaAutoRefusalRepository, IdeaAutoRefusalRepository>();
        services.AddScoped<IIdeaSignalRepository, IdeaSignalRepository>();
        services.AddScoped<IIdeaExitRuleRepository, IdeaExitRuleRepository>();
        services.AddScoped<IIdeaBucketRepository, IdeaBucketRepository>();
        services.AddScoped<IIdeaCalibrationRepository, IdeaCalibrationRepository>();
        services.AddScoped<IIdeaReplayRepository, IdeaReplayRepository>();
        services.AddScoped<IIdeaVolatilityRepository, IdeaVolatilityRepository>();
        services.AddScoped<ISavedFilterRepository, SavedFilterRepository>();

        // The gate of the scanner's REST requests and its lease lane are singletons; the repository keeps nothing but the data source.
        services.AddSingleton<IScannerStateRepository, ScannerStateRepository>();
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(_ => ConnectionString(configuration))
                .ScanIn(typeof(PostgresRegistrar).Assembly)
                .For.Migrations());
    }

    private static string ConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("MarketData")!;
    }
}