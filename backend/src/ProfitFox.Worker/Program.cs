using Grpc.Net.Client;

using ProfitFox.Application.Commands.Hypotheses;
using ProfitFox.Application.Commands.Runs;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Runs;
using ProfitFox.Infrastructure.MarketData;
using ProfitFox.Infrastructure.Persistence;

namespace ProfitFox.Worker;

/// <summary>
///     Computes the runs of the bench queue (spec bench-run-queue): takes a run of the database with a lease, reads its history
///     from the market-data service and writes its line in the ledger. Any number of Workers share one database. The API
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
    ///     The services of the Worker; a test host replaces the market-data service afterwards.
    /// </summary>
    public static void Configure(HostApplicationBuilder builder)
    {
        var grpc = new Uri(Setting(builder.Configuration, "MarketData:GrpcUrl"));
        var rest = new Uri(Setting(builder.Configuration, "MarketData:RestUrl"));
        string connectionString = Setting(builder.Configuration, "ConnectionStrings:Bench");
        IServiceCollection services = builder.Services;

        services.AddSingleton(builder.Configuration.GetSection("Worker").Get<WorkerSettings>() ?? new WorkerSettings());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => GrpcChannel.ForAddress(grpc, new GrpcChannelOptions { MaxReceiveMessageSize = 64 * 1024 * 1024 }));
        services.AddSingleton<IHistorySource>(provider => new GrpcHistorySource(provider.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IInstrumentCatalog>(_ => new InstrumentCatalog(new HttpClient { BaseAddress = rest, Timeout = TimeSpan.FromMinutes(1) }));
        services.AddBenchDatabase(connectionString);
        services.AddSingleton<HypothesisCatalog>();
        services.AddSingleton(provider => new HypothesisRunner(
            provider.GetRequiredService<HypothesisCatalog>(),
            provider.GetRequiredService<IHistorySource>(),
            provider.GetRequiredService<IInstrumentCatalog>(),
            provider.GetRequiredService<IRunLedger>(),
            LogWriter.Of<HypothesisRunner>(provider)));
        services.AddSingleton(provider => new RunExecutor(
            provider.GetRequiredService<IRunQueue>(),
            provider.GetRequiredService<HypothesisRunner>(),
            provider.GetRequiredService<WorkerSettings>(),
            provider.GetRequiredService<TimeProvider>(),
            LogWriter.Of<RunExecutor>(provider)));
        services.AddHostedService<RunBackgroundService>();
    }

    private static string Setting(IConfiguration configuration, string key)
    {
        return configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The setting {key} is not set.");
    }
}
