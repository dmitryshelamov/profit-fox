using ProfitFox.Application.Commands.Runs.RunNext;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Infrastructure;
using ProfitFox.Worker.BackgroundServices;

namespace ProfitFox.Worker;

/// <summary>
///     Computes the backtests of the bench queue (spec bench-run-queue): takes a backtest of the database with a lease, reads
///     its history from the market-data service and writes its numbers. Any number of Workers share one database. The API
///     migrates the database; a Worker waits until it has.
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
        services.AddHostedService<RunBackgroundService>();
        services.AddHostedService<IdeaScanBackgroundService>();
    }
}
