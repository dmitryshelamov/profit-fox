using ProfitFox.Application.Commands.Runs.RunNext;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Infrastructure;
using ProfitFox.Worker.BackgroundServices;

namespace ProfitFox.Worker;

/// <summary>
///     Computes the backtests of the bench queue (spec bench-run-queue): takes a backtest of the database with a lease, reads
///     its history from the market-data service and writes its numbers. Any number of Workers share one database. The API
///     migrates the database; a Worker waits until it has. With <c>Trading:Enabled</c> it may be the trading Worker of the
///     trades by ideas (change take-idea-trades); it then needs the master key of the secrets of the accounts.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        Configure(builder);
        await builder.Build().RunAsync();
    }

    /// <summary>
    ///     The services of the Worker on top of those of <c>AddInfrastructure</c>; a test host replaces the market-data service
    ///     afterwards.
    /// </summary>
    public static void Configure(HostApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        services.AddInfrastructure(builder.Configuration);
        services.AddSingleton(builder.Configuration.GetSection("Worker").Get<WorkerSettings>() ?? new WorkerSettings());
        services.AddSingleton(builder.Configuration.GetSection("Scanner").Get<ScannerSettings>() ?? new ScannerSettings());
        services.AddSingleton(builder.Configuration.GetSection("Ideas").Get<IdeasSettings>() ?? new IdeasSettings());
        TradingSettings trading = builder.Configuration.GetSection("Trading").Get<TradingSettings>() ?? new TradingSettings();
        if (trading.Problems() is { Count: > 0 } problems)
        {
            // A drift of 0.5 meant as percent would be 50 %: the Worker does not start (review of the application, R7).
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        services.AddSingleton(trading);
        if (trading.Enabled)
        {
            // Only the trading Worker decrypts the secrets of the accounts (change take-idea-trades, design.md, decision 2).
            services.RequireMasterKey();
        }

        services.AddHostedService<RunBackgroundService>();
        services.AddHostedService<IdeaScanBackgroundService>();
        services.AddHostedService<TradeBackgroundService>();
    }
}
